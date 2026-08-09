using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class CloseShiftCommand : IRequest<ApiResponse<object>>
{
    public Guid TripId { get; set; }
    public Guid WarehouseId { get; set; }
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

        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Không tìm thấy chuyến xe với ID '{request.TripId}'.");

        if (trip.Status == "COMPLETED" || trip.Status == "CANCELLED")
            throw new ValidationException($"Chuyến xe này đã ở trạng thái {trip.Status}, không thể đóng ca.");

        var tripDrivers = await _context.TripDrivers
            .Where(td => td.TripId == trip.TripId)
            .ToListAsync(cancellationToken);

        var pendingOrders = await _context.TransportOrders
            .Where(o => o.MasterTripId == trip.TripId)
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
                            d.Status = "ACTIVE";
                            await _driverAvailability.ReconcileStatusAsync(d);
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
                    Message = "Đã đóng ca thành công, tài xế và xe đã được cập nhật trạng thái ACTIVE và sẵn sàng ghép chuyến mới."
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
}
