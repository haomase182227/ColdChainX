using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class CloseShiftCommand : IRequest<ApiResponse<object>>
{
    public Guid TripId { get; set; }
    public Guid WarehouseId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Guid UserId { get; set; }
}

public class CloseShiftCommandHandler : IRequestHandler<CloseShiftCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDeliveryEventService _deliveryEvents;
    private readonly IDriverAvailabilityService _driverAvailability;

    public CloseShiftCommandHandler(
        IApplicationDbContext context,
        IDeliveryEventService deliveryEvents,
        IDriverAvailabilityService driverAvailability)
    {
        _context = context;
        _deliveryEvents = deliveryEvents;
        _driverAvailability = driverAvailability;
    }

    public async Task<ApiResponse<object>> Handle(CloseShiftCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.WarehouseId == request.WarehouseId, cancellationToken);
        if (warehouse == null)
            throw new NotFoundException($"Không tìm thấy kho bãi với ID '{request.WarehouseId}'.");
        if (!string.IsNullOrWhiteSpace(warehouse.Status)
            && !warehouse.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
            && !warehouse.Status.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Kho '{warehouse.WarehouseName}' hiện không hoạt động và không thể nhận hàng trả về.");
        }

        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Không tìm thấy chuyến xe với ID '{request.TripId}'.");

        if (trip.Status == "COMPLETED" || trip.Status == "CANCELLED")
            throw new ValidationException($"Chuyến xe này đã ở trạng thái {trip.Status}, không thể đóng ca.");

        var tripDrivers = await _context.TripDrivers
            .Where(td => td.TripId == trip.TripId)
            .ToListAsync(cancellationToken);

        var currentDriver = await _context.Drivers
            .FirstOrDefaultAsync(driver => driver.UserId == request.UserId, cancellationToken);
        if (currentDriver == null
            || !tripDrivers.Any(tripDriver => tripDriver.DriverId == currentDriver.DriverId))
        {
            throw new ForbiddenException("Bạn không phải tài xế được phân công cho chuyến này.");
        }

        var pendingOrders = await _context.TransportOrders
            .Where(order =>
                order.MasterTripId == trip.TripId
                || _context.Lpns.Any(lpn =>
                    lpn.TripId == trip.TripId && lpn.OrderId == order.OrderId))
            .ToListAsync(cancellationToken);

        if (pendingOrders.Any())
        {
            var orderIds = pendingOrders.Select(o => o.OrderId).ToList();
            var confirmedEpodOrderIds = await _context.DeliveryEpods
                .Where(e => e.OrderId.HasValue && orderIds.Contains(e.OrderId.Value) && e.HandoverConfirmedAt != null)
                .Select(e => e.OrderId!.Value)
                .ToListAsync(cancellationToken);

            var unconfirmedOrders = pendingOrders
                .Where(o => !confirmedEpodOrderIds.Contains(o.OrderId))
                .Select(o => o.TrackingCode)
                .ToList();

            if (unconfirmedOrders.Any())
            {
                throw new ValidationException(
                    $"Không thể đóng ca. Còn các đơn hàng chưa hoàn tất giao/nhận: {string.Join(", ", unconfirmedOrders)}. " +
                    "Vui lòng hoàn tất đồng kiểm và bàn giao trước khi đóng ca.");
            }
        }

        var closeTime = DateTime.UtcNow;
        var strategy = _context.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var noShowReturnIncident = await PrepareNoShowReturnInboundAsync(
                    request,
                    trip,
                    warehouse,
                    pendingOrders,
                    closeTime,
                    cancellationToken);

                trip.Status = "COMPLETED";
                trip.CompletedAt = closeTime;

                if (trip.VehicleId != null)
                {
                    var vehicle = await _context.Vehicles
                        .FirstOrDefaultAsync(v => v.VehicleId == trip.VehicleId.Value, cancellationToken);
                    if (vehicle != null)
                    {
                        vehicle.Status = "ACTIVE";
                        vehicle.CurrentLocation = warehouse.WarehouseId.ToString();
                    }
                }

                foreach (var td in tripDrivers)
                {
                    var d = await _context.Drivers
                        .Include(dr => dr.DriverLicenses)
                        .FirstOrDefaultAsync(dr => dr.DriverId == td.DriverId, cancellationToken);
                        
                    if (d != null)
                    {
                        d.CurrentLocation = warehouse.WarehouseId.ToString();
                        
                        var today = DateOnly.FromDateTime(closeTime);
                        var hasValidLicense = d.DriverLicenses.Any(l =>
                            l.ExpiryDate >= today
                            && (string.IsNullOrWhiteSpace(l.Status)
                                || l.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)));

                        if (!hasValidLicense)
                        {
                            d.Status = "SUSPENDED_DOCS";
                        }
                        else
                        {
                            d.Status = "RELAX";
                        }
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                await _deliveryEvents.NotifyTripCompletedAsync(
                    trip.TripId,
                    trip.TripId.ToString("N")[..8].ToUpper(),
                    closeTime,
                    cancellationToken);

                var response = new 
                {
                    TripId = trip.TripId,
                    ClosedAt = closeTime,
                    VehicleReleased = trip.VehicleId != null,
                    DriversReleasedCount = tripDrivers.Count,
                    NewLocation = warehouse.WarehouseName,
                    NoShowReturnIncidentId = noShowReturnIncident?.IncidentId,
                    RequiresWarehouseInboundBySeal = noShowReturnIncident != null,
                    Message = noShowReturnIncident == null
                        ? "Đã đóng ca thành công. Xe đã sẵn sàng; tài xế chuyển sang RELAX trong 4 giờ trước khi nhận chuyến mới."
                        : $"Đã ghi nhận hàng khách vắng mặt đang trở về {warehouse.WarehouseName}. Nhân viên kho nhập lại bằng seal tại màn hình Inbound hàng sự cố; không cần QC."
                };

                return ApiResponse<object>.SuccessResponse(response, "Đóng ca thành công.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task<IncidentReport?> PrepareNoShowReturnInboundAsync(
        CloseShiftCommand request,
        MasterTrip trip,
        ColdChainX.Core.Entities.Warehouse warehouse,
        System.Collections.Generic.IReadOnlyCollection<TransportOrder> tripOrders,
        DateTime plannedAt,
        CancellationToken cancellationToken)
    {
        var noShowOrders = tripOrders
            .Where(order => string.Equals(
                order.Status,
                "DELIVERY_FAILED_NOSHOW",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (noShowOrders.Count == 0)
            return null;

        var noShowOrderIds = noShowOrders.Select(order => order.OrderId).ToList();
        var returnLpns = await _context.Lpns
            .Where(lpn => noShowOrderIds.Contains(lpn.OrderId))
            .ToListAsync(cancellationToken);

        var ordersWithoutLpns = noShowOrders
            .Where(order => returnLpns.All(lpn => lpn.OrderId != order.OrderId))
            .Select(order => order.TrackingCode)
            .ToList();
        if (ordersWithoutLpns.Count > 0)
        {
            throw new ValidationException(
                $"Không thể đóng ca: đơn No-Show chưa có LPN để nhập lại kho: {string.Join(", ", ordersWithoutLpns)}.");
        }

        var invalidReturnLpns = returnLpns
            .Where(lpn => lpn.State != LpnState.RETURN_PENDING)
            .Select(lpn => lpn.LpnCode)
            .ToList();
        if (invalidReturnLpns.Count > 0)
        {
            throw new ValidationException(
                $"Không thể đóng ca: LPN No-Show phải ở trạng thái RETURN_PENDING: {string.Join(", ", invalidReturnLpns)}.");
        }

        var reporter = await _context.Users
            .FirstOrDefaultAsync(user => user.UserId == request.UserId, cancellationToken);
        if (reporter == null)
            throw new ForbiddenException("Không tìm thấy tài khoản tài xế đang lập kế hoạch trả hàng No-Show.");

        var assignedDriver = await _context.TripDrivers
            .Include(tripDriver => tripDriver.Driver)
            .Where(tripDriver => tripDriver.TripId == trip.TripId
                                 && tripDriver.Driver.UserId == request.UserId)
            .Select(tripDriver => tripDriver.Driver)
            .FirstOrDefaultAsync(cancellationToken);

        var vehicle = trip.VehicleId.HasValue
            ? await _context.Vehicles.FirstOrDefaultAsync(
                item => item.VehicleId == trip.VehicleId.Value,
                cancellationToken)
            : null;

        var plan = new ExternalReeferPlanRecord
        {
            RentalProvider = "ColdChainX Driver Return",
            VehiclePlate = vehicle?.TruckPlate ?? "ColdChainX Vehicle",
            DriverName = assignedDriver?.FullName ?? reporter.FullName,
            DriverPhone = assignedDriver?.PhoneNumber ?? reporter.Phone,
            DestinationWarehouseId = warehouse.WarehouseId,
            DestinationWarehouseName = warehouse.WarehouseName,
            DestinationWarehouseAddress = warehouse.Address,
            RouteDestinationCity = warehouse.Address,
            AgreedTemperature = trip.TargetTemperature,
            OriginalTripId = trip.TripId,
            DispatchedAt = plannedAt,
            ExpectedWarehouseArrivalAt = plannedAt,
            SealNumber = ResolveReturnSeal(trip.SealNumber),
            LpnIds = returnLpns.Select(lpn => lpn.LpnId).Distinct().ToList(),
            RecordedBy = reporter.UserId,
            DispatchNote = $"Khách vắng mặt; tài xế chọn trả hàng về {warehouse.WarehouseName}. Warehouse inbound bằng seal và bỏ qua QC."
        };

        var incidentType = IncidentType.CUSTOMER_NO_SHOW_RETURN.ToString();
        var incident = await _context.IncidentReports
            .FirstOrDefaultAsync(item => item.TripId == trip.TripId
                                         && item.IncidentType == incidentType
                                         && item.Status != "RETURNED_TO_HUB",
                cancellationToken);

        if (incident == null)
        {
            incident = new IncidentReport
            {
                IncidentId = Guid.NewGuid(),
                TripId = trip.TripId,
                IncidentType = incidentType,
                Severity = "LOW",
                RiskLevel = IncidentRiskLevel.LOW.ToString(),
                Description = $"Khách không có mặt nhận hàng. Trả {returnLpns.Count} LPN về {warehouse.WarehouseName}.",
                RequiresRescue = false,
                DriverPaidAmount = 0m,
                ExpenseStatus = "NOT_REQUIRED",
                ReportedBy = reporter.UserId,
                ReportedAt = plannedAt
            };
            _context.IncidentReports.Add(incident);
        }

        incident.Status = "EXTERNAL_REEFER_IN_TRANSIT";
        incident.RescuePlanType = IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString();
        incident.RescuePlanDetails = JsonSerializer.Serialize(plan);
        incident.RedispatchPlan = null;
        incident.HandledBy = null;
        incident.HandledAt = null;
        incident.ResolvedBy = null;
        incident.ResolvedAt = null;
        incident.ResolutionNote = null;

        return incident;
    }

    private static string ResolveReturnSeal(string? sealNumber)
    {
        if (string.IsNullOrWhiteSpace(sealNumber))
            return string.Empty;

        return sealNumber.Contains("UNSEALED", StringComparison.OrdinalIgnoreCase)
               || sealNumber.Contains("ĐÃ CẮT", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : sealNumber.Trim();
    }
}
