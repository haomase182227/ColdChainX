using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services;

public class IncidentRescueService : IIncidentRescueService
{
    private readonly ApplicationDbContext _db;
    private readonly IGoongMapService _goongMapService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IMqttCommandPublisher _mqttPublisher;
    private readonly ILogger<IncidentRescueService> _logger;
    private readonly INotificationService? _notificationService;
    private readonly ILocationService? _locationService;

    private static readonly string[] OnRoadTripStatuses = { "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private const string RescueDispatchedStatus = "RESCUE_DISPATCHED";
    private const string DelayedTemplateId = "INCIDENT_TRIP_DELAYED";
    private const string DefaultHandlingNote =
        "Tài xế xác nhận đã xử lý sự cố tại chỗ và tiếp tục hành trình.";
    private const int DefaultTransloadMinutes = 45;
    private const decimal FallbackAvgSpeedKmh = 40m;

    public IncidentRescueService(
        ApplicationDbContext db,
        IGoongMapService goongMapService,
        IHubContext<NotificationHub> hubContext,
        IMqttCommandPublisher mqttPublisher,
        ILogger<IncidentRescueService> logger,
        INotificationService? notificationService = null,
        ILocationService? locationService = null)
    {
        _db = db;
        _goongMapService = goongMapService;
        _hubContext = hubContext;
        _mqttPublisher = mqttPublisher;
        _logger = logger;
        _notificationService = notificationService;
        _locationService = locationService;
    }


    public async Task<ApiResponse<List<RescueCandidateResponse>>> GetRescueCandidatesAsync(Guid incidentId)
    {
        try
        {
            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Không tìm thấy báo cáo sự cố.");
            if (!incident.TripId.HasValue)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố không gắn với chuyến hàng nào.");
            if (!incident.RequiresRescue)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố này không yêu cầu xe cứu hộ.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Sự cố đã được xử lý xong.");

            var trip = await _db.MasterTrips.FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null)
                return ApiResponse<List<RescueCandidateResponse>>.Failure("Không tìm thấy chuyến hàng của sự cố.");

            var load = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .GroupBy(l => 1)
                .Select(g => new { Weight = g.Sum(l => l.ActualWeightKg), Cbm = g.Sum(l => l.ActualCbm) })
                .FirstOrDefaultAsync();
            var totalWeight = load?.Weight ?? 0m;
            var totalCbm = load?.Cbm ?? 0m;

            var vehicles = await _db.Vehicles
                .Include(v => v.IotDevices)
                .Where(v => v.Status == "ACTIVE"
                         && v.VehicleId != trip.VehicleId
                         && v.MinTemp <= trip.TargetTemperature
                         && v.MaxTemp >= trip.TargetTemperature
                         && v.MaxWeight >= totalWeight
                         && v.MaxCbm >= totalCbm
                         && v.IotDevices.Any(d => d.DeviceCode != null && d.DeviceCode != ""))
                .ToListAsync();

            var warehouseIds = vehicles
                .Select(v => ParseWarehouseId(v.CurrentLocation))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
            var warehouses = await _db.Warehouses
                .AsNoTracking()
                .Where(w => warehouseIds.Contains(w.WarehouseId))
                .ToDictionaryAsync(w => w.WarehouseId);
            var warehouseCoordinates = await ResolveWarehouseCoordinatesAsync(warehouses.Values);

            var items = vehicles.Select(v =>
            {
                var warehouseId = ParseWarehouseId(v.CurrentLocation);
                var warehouse = warehouseId.HasValue && warehouses.TryGetValue(warehouseId.Value, out var matchedWarehouse)
                    ? matchedWarehouse
                    : null;
                decimal? distanceKm = null;
                if (warehouse != null
                    && incident.CurrentLatitude.HasValue
                    && incident.CurrentLongitude.HasValue
                    && warehouseCoordinates.TryGetValue(warehouse.WarehouseId, out var coordinates))
                {
                    distanceKm = Math.Round(HaversineKm(
                        incident.CurrentLatitude.Value,
                        incident.CurrentLongitude.Value,
                        coordinates.Latitude,
                        coordinates.Longitude), 2);
                }

                var iotDeviceCount = v.IotDevices.Count(d => !string.IsNullOrWhiteSpace(d.DeviceCode));
                return new RescueCandidateResponse
                {
                    VehicleId = v.VehicleId,
                    TruckPlate = v.TruckPlate,
                    VehicleType = v.VehicleType,
                    WarehouseId = warehouse?.WarehouseId,
                    WarehouseName = warehouse?.WarehouseName,
                    WarehouseAddress = warehouse?.Address,
                    DistanceKm = distanceKm,
                    MaxWeight = v.MaxWeight,
                    MaxCbm = v.MaxCbm,
                    MinTemp = v.MinTemp,
                    MaxTemp = v.MaxTemp,
                    IotDeviceCount = iotDeviceCount,
                    OnlineIotDeviceCount = v.IotDevices.Count(d => !string.IsNullOrWhiteSpace(d.DeviceCode) && d.IsOnline),
                    HasOnlineIot = v.IotDevices.Any(d => !string.IsNullOrWhiteSpace(d.DeviceCode) && d.IsOnline),
                    Label = $"{v.TruckPlate} — {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m³ | nhiệt {v.MinTemp}..{v.MaxTemp}°C | IoT {iotDeviceCount}"
                };
            })
            .OrderBy(item => item.DistanceKm.HasValue ? 0 : 1)
            .ThenBy(item => item.DistanceKm)
            .ThenBy(item => item.MaxWeight)
            .ToList();

            return ApiResponse<List<RescueCandidateResponse>>.SuccessResponse(
                items,
                items.Count == 0
                    ? "Không có xe thay thế phù hợp"
                    : $"Tìm thấy {items.Count} xe đủ điều kiện thay thế (cần chở {totalWeight}kg / {totalCbm}m³ ở {trip.TargetTemperature}°C).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rescue candidates. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<List<RescueCandidateResponse>>.Failure($"Failed to get rescue candidates: {ex.Message}");
        }
    }

    private async Task<Dictionary<Guid, (decimal Latitude, decimal Longitude)>> ResolveWarehouseCoordinatesAsync(
        IEnumerable<Warehouse> warehouses)
    {
        var result = new Dictionary<Guid, (decimal Latitude, decimal Longitude)>();
        var warehouseList = warehouses.ToList();
        var addresses = warehouseList
            .Select(w => w.Address?.Trim())
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var knownLocations = addresses.Count == 0
            ? new List<Location>()
            : await _db.Locations
                .AsNoTracking()
                .Where(location => addresses.Contains(location.Address))
                .ToListAsync();
        var coordinatesByAddress = knownLocations
            .Where(location => IsValidCoordinates(location.Latitude, location.Longitude))
            .GroupBy(location => location.Address.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (group.First().Latitude, group.First().Longitude),
                StringComparer.OrdinalIgnoreCase);

        foreach (var warehouse in warehouseList)
        {
            if (TryParseCoordinates(warehouse.Address, out var coordinates))
            {
                result[warehouse.WarehouseId] = coordinates;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(warehouse.Address)
                && coordinatesByAddress.TryGetValue(warehouse.Address.Trim(), out coordinates))
            {
                result[warehouse.WarehouseId] = coordinates;
                continue;
            }

            if (_locationService == null || string.IsNullOrWhiteSpace(warehouse.Address))
                continue;

            try
            {
                var resolved = await _locationService.GetCoordinatesAsync(warehouse.Address);
                if (IsValidCoordinates(resolved.Latitude, resolved.Longitude))
                    result[warehouse.WarehouseId] = resolved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not resolve coordinates for warehouse {WarehouseId} ({WarehouseAddress}).",
                    warehouse.WarehouseId,
                    warehouse.Address);
            }
        }

        return result;
    }

    private static Guid? ParseWarehouseId(string? currentLocation)
        => Guid.TryParse(currentLocation, out var warehouseId) ? warehouseId : null;

    private static bool TryParseCoordinates(
        string? value,
        out (decimal Latitude, decimal Longitude) coordinates)
    {
        coordinates = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var latitude)
            || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var longitude)
            || !IsValidCoordinates(latitude, longitude))
        {
            return false;
        }

        coordinates = (latitude, longitude);
        return true;
    }

    private static bool IsValidCoordinates(decimal latitude, decimal longitude)
        => latitude is >= -90m and <= 90m && longitude is >= -180m and <= 180m;


    public async Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(
        Guid incidentId,
        ContinueTripAfterIncidentRequest request,
        Guid driverUserId)
    {
        var handlingNote = string.IsNullOrWhiteSpace(request.HandlingNote)
            ? DefaultHandlingNote
            : request.HandlingNote.Trim();

        try
        {
            var driver = await _db.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == driverUserId);
            if (driver == null)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Tài khoản hiện tại không có hồ sơ tài xế.",
                    403);
            }

            var incident = await _db.IncidentReports
                .Include(i => i.Trip)
                    .ThenInclude(t => t!.Vehicle)
                .FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (incident.RequiresRescue)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Sự cố yêu cầu xe cứu hộ; hãy dùng rescue-candidates và dispatch-rescue.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố đã được đóng.");
            if (!incident.TripId.HasValue || incident.Trip == null || incident.Trip.Vehicle == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố không gắn với chuyến/xe hợp lệ.");

            var trip = incident.Trip;
            var isAssignedDriver = await _db.TripDrivers
                .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId);
            if (!isAssignedDriver)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Bạn không phải tài xế được phân công cho chuyến này.",
                    403);
            }

            if (incident.Status == "CONTINUED" && trip.Status == "IN_TRANSIT")
            {
                return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                    BuildWorkflowResult(incident, trip, trip.Vehicle, incident.HandledAt ?? DbNow(),
                        "Chuyến đã được cho tiếp tục trước đó."),
                    "Trip already continued.");
            }

            if (incident.Status != "REPORTED")
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Sự cố đang ở trạng thái {incident.Status ?? "UNKNOWN"} và không thể tiếp tục theo nhánh tự xử lý.");
            }

            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Chuyến đang ở trạng thái {trip.Status ?? "UNKNOWN"} và không thể tiếp tục từ luồng sự cố.");

            var now = DbNow();
            trip.Status = "IN_TRANSIT";
            incident.Status = "CONTINUED";
            incident.HandledBy = driverUserId;
            incident.HandledAt = now;
            incident.HandlingNote = handlingNote;

            await _db.SaveChangesAsync();

            try
            {
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                    .SendAsync("IncidentTripContinued", new
                    {
                        incident.IncidentId,
                        trip.TripId,
                        trip.Status,
                        VehicleId = trip.Vehicle.VehicleId,
                        VehiclePlate = trip.Vehicle.TruckPlate,
                        incident.HandlingNote,
                        incident.HandledAt
                    });
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(
                    hubEx,
                    "SignalR push failed after continuing incident trip. IncidentId: {IncidentId}",
                    incidentId);
            }

            return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                BuildWorkflowResult(
                    incident,
                    trip,
                    trip.Vehicle,
                    now,
                    "Tài xế đã ghi nhận xử lý tại chỗ và tiếp tục chuyến."),
                "Driver continued trip successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to continue trip after incident. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentWorkflowResult>.Failure(
                $"Failed to continue trip after incident: {ex.Message}");
        }
    }


    public async Task<ApiResponse<IncidentRescueResult>> DispatchRescueAsync(
        Guid incidentId, DispatchRescueRequest request, Guid dispatcherId)
    {
        if (request == null || request.ReplacementVehicleId == Guid.Empty)
            return ApiResponse<IncidentRescueResult>.Failure("Vui lòng chọn xe thay thế (ReplacementVehicleId).");

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var dispatcherExists = await _db.Users.AnyAsync(u => u.UserId == dispatcherId);
            if (!dispatcherExists)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy tài khoản điều phối viên.");

            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố đã được xử lý xong trước đó.");
            if (incident.Status == RescueDispatchedStatus)
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố này đã có lệnh điều xe cứu hộ.");
            if (!incident.RequiresRescue)
                return ApiResponse<IncidentRescueResult>.Failure(
                    "Sự cố này không yêu cầu xe cứu hộ.");
            if (!incident.TripId.HasValue)
                return ApiResponse<IncidentRescueResult>.Failure("Sự cố không gắn với chuyến hàng nào — không thể điều xe thay thế.");

            var trip = await _db.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripStops)
                    .ThenInclude(s => s.Location)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy chuyến hàng của sự cố.");

            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Chuyến đang ở trạng thái {trip.Status ?? "UNKNOWN"} — chỉ điều xe thay thế khi hàng đang trên đường " +
                    $"({string.Join("/", OnRoadTripStatuses)}). Nếu chưa xuất phát, hãy dùng API hủy/ghép lại chuyến.");

            var brokenVehicle = trip.Vehicle;
            if (brokenVehicle == null)
                return ApiResponse<IncidentRescueResult>.Failure("Chuyến không có xe đang gán — dữ liệu không hợp lệ.");

            if (request.ReplacementVehicleId == brokenVehicle.VehicleId)
                return ApiResponse<IncidentRescueResult>.Failure("Xe thay thế phải khác xe đang gặp sự cố.");

            var rescueVehicle = await _db.Vehicles
                .Include(v => v.IotDevices)
                .FirstOrDefaultAsync(v => v.VehicleId == request.ReplacementVehicleId);
            if (rescueVehicle == null)
                return ApiResponse<IncidentRescueResult>.Failure("Không tìm thấy xe thay thế.");
            if (rescueVehicle.Status != "ACTIVE")
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} đang ở trạng thái {rescueVehicle.Status ?? "UNKNOWN"} — chỉ điều được xe ACTIVE.");

            if (trip.TargetTemperature < rescueVehicle.MinTemp || trip.TargetTemperature > rescueVehicle.MaxTemp)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} không giữ được nhiệt độ {trip.TargetTemperature}°C " +
                    $"(dải nhiệt của xe: {rescueVehicle.MinTemp}..{rescueVehicle.MaxTemp}°C).");

            var rescueDevices = rescueVehicle.IotDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceCode))
                .ToList();
            if (rescueDevices.Count == 0)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} chưa có thiết bị IoT riêng và không thể được điều cứu hộ.");

            var shippingLpns = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .ToListAsync();
            var totalWeight = shippingLpns.Sum(l => l.ActualWeightKg);
            var totalCbm = shippingLpns.Sum(l => l.ActualCbm);
            if (totalWeight > rescueVehicle.MaxWeight || totalCbm > rescueVehicle.MaxCbm)
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} không đủ tải để sang hàng: cần {totalWeight}kg / {totalCbm}m³, " +
                    $"xe chỉ chở tối đa {rescueVehicle.MaxWeight}kg / {rescueVehicle.MaxCbm}m³.");

            var now = DbNow();

            brokenVehicle.Status = "MAINTENANCE";
            if (incident.CurrentLatitude.HasValue && incident.CurrentLongitude.HasValue)
                brokenVehicle.CurrentLocation = GoongMapService.FormatCoordinate(
                    incident.CurrentLatitude.Value, incident.CurrentLongitude.Value);

            var ticket = new MaintenanceTicket
            {
                TicketId = Guid.NewGuid(),
                TicketCode = $"MT-{DateTime.Now:yyyyMMddHHmmss}",
                VehicleId = brokenVehicle.VehicleId,
                MaintenanceType = "INCIDENT_BREAKDOWN",
                TriggeredAtOdometer = brokenVehicle.CurrentOdometer,
                GarageName = "Cứu hộ tại hiện trường",
                Description = $"Sự cố {incident.IncidentType} trên chuyến {trip.TripId}: {incident.Description}",
                IssueDate = DateOnly.FromDateTime(DateTime.Today),
                Status = "OPEN",
                CreatedBy = dispatcherId,
                CreatedAt = now
            };
            _db.MaintenanceTickets.Add(ticket);

            trip.VehicleId = rescueVehicle.VehicleId;
            rescueVehicle.Status = "ONTRIP";

            trip.Status = "DELAYED";
            incident.Status = RescueDispatchedStatus;
            incident.HandledBy = dispatcherId;
            incident.HandledAt = now;
            incident.HandlingNote = request.Note?.Trim();
            incident.BrokenVehicleId = brokenVehicle.VehicleId;
            incident.ReplacementVehicleId = rescueVehicle.VehicleId;
            incident.MaintenanceTicketId = ticket.TicketId;
            incident.RescueDispatchedAt = now;

            var transloadMinutes = request.TransloadMinutes is > 0 ? request.TransloadMinutes.Value : DefaultTransloadMinutes;
            var departFromScene = now.AddMinutes(transloadMinutes);

            var remainingStops = trip.TripStops
                .Where(s => s.ActualArrivalTime == null
                         && s.Status != "CANCELLED"
                         && s.Status != "COMPLETED"
                         && s.Status != "ARRIVED")
                .OrderBy(s => s.StopSequence)
                .ToList();

            var (etaMethod, stopChanges) = await RecalculateEtaAsync(incident, remainingStops, departFromScene);

            if (remainingStops.Count > 0)
                trip.PlannedEndTime = remainingStops[^1].PlannedDepartureTime;

            var tripOrders = await _db.TransportOrders
                .Include(o => o.Customer)
                .Where(o => o.MasterTripId == trip.TripId)
                .ToListAsync();

            var templateId = _notificationService == null
                ? await GetOrCreateTemplateAsync(
                    DelayedTemplateId,
                    "Chuyến hàng {{tracking_code}} dự kiến trễ {{delay_minutes}} phút do sự cố vận chuyển",
                    "Xe {{old_plate}} gặp sự cố ({{incident_type}}) trên đường giao hàng. " +
                    "Chúng tôi đã lập tức điều xe lạnh {{new_plate}} đến thay thế để đảm bảo chất lượng hàng hóa. " +
                    "Ngày giao dự kiến mới: {{new_eta}} (kế hoạch cũ: {{old_eta}}). " +
                    "Thành thật xin lỗi quý khách vì sự bất tiện này.")
                : null;

            var notifiedUserIds = new List<Guid>();
            var customerPushTargets = new List<(Guid UserId, Guid OrderId)>();
            var customerUserCache = new Dictionary<Guid, Guid?>();

            foreach (var change in stopChanges)
            {
                var stop = remainingStops.First(s => s.StopId == change.StopId);
                if (!stop.LocationId.HasValue) continue;

                var stopOrders = tripOrders.Where(o => o.DestLocation == stop.LocationId.Value).ToList();
                foreach (var order in stopOrders)
                {
                    var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId, customerUserCache);
                    if (!customerUserId.HasValue) continue;

                    var notifParams = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "tracking_code", order.TrackingCode },
                        { "incident_type", incident.IncidentType },
                        { "old_plate",     brokenVehicle.TruckPlate },
                        { "new_plate",     rescueVehicle.TruckPlate },
                        { "old_eta",       FormatCustomerEtaDate(change.OldEta) },
                        { "new_eta",       FormatCustomerEtaDate(change.NewEta) },
                        { "delay_minutes", change.DelayMinutes.ToString(CultureInfo.InvariantCulture) }
                    });

                    if (_notificationService == null)
                    {
                        if (templateId == null)
                            continue;

                        _db.Notifications.Add(new Notification
                        {
                            NotiId = Guid.NewGuid(),
                            UserId = customerUserId.Value,
                            SenderId = dispatcherId,
                            TemplateId = templateId,
                            Params = notifParams,
                            OrderId = order.OrderId,
                            IsRead = false,
                            CreatedAt = now
                        });
                    }
                    else
                    {
                        customerPushTargets.Add((customerUserId.Value, order.OrderId));
                    }

                    change.NotifiedCustomers++;
                    notifiedUserIds.Add(customerUserId.Value);
                }
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await SendFirebaseRescueNotificationsAsync(
                incident,
                trip,
                rescueVehicle,
                dispatcherId,
                customerPushTargets);

            try
            {
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_WarehouseWorker", "Group_Admin")
                    .SendAsync("IncidentRescueDispatched", new
                    {
                        IncidentId = incident.IncidentId,
                        TripId = trip.TripId,
                        BrokenVehiclePlate = brokenVehicle.TruckPlate,
                        RescueVehiclePlate = rescueVehicle.TruckPlate,
                        Latitude = incident.CurrentLatitude,
                        Longitude = incident.CurrentLongitude,
                        TransloadLpnCount = shippingLpns.Count,
                        Note = request.Note,
                        Message = $"Sang toàn bộ {shippingLpns.Count} LPN từ xe {brokenVehicle.TruckPlate} sang xe {rescueVehicle.TruckPlate} tại hiện trường."
                    });

                foreach (var userId in notifiedUserIds.Distinct())
                {
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("TripDelayed", new
                    {
                        TripId = trip.TripId,
                        NewVehiclePlate = rescueVehicle.TruckPlate,
                        Stops = stopChanges.Select(c => new { c.Address, c.NewEta, c.DelayMinutes })
                    });
                }
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(hubEx, "SignalR push failed after rescue dispatch. IncidentId: {IncidentId}", incidentId);
            }

            var result = new IncidentRescueResult
            {
                IncidentId = incident.IncidentId,
                IncidentStatus = incident.Status!,
                TripId = trip.TripId,
                TripStatus = trip.Status!,
                BrokenVehicleId = brokenVehicle.VehicleId,
                BrokenVehiclePlate = brokenVehicle.TruckPlate,
                BrokenVehicleStatus = brokenVehicle.Status!,
                MaintenanceTicketId = ticket.TicketId,
                RescueVehicleId = rescueVehicle.VehicleId,
                RescueVehiclePlate = rescueVehicle.TruckPlate,
                RescueVehicleStatus = rescueVehicle.Status!,
                TransloadLpnCount = shippingLpns.Count,
                EtaMethod = etaMethod,
                UpdatedStops = stopChanges,
                NotifiedCustomerCount = notifiedUserIds.Distinct().Count(),
                Message = $"Đã điều xe {rescueVehicle.TruckPlate} thay thế xe {brokenVehicle.TruckPlate} (sang {shippingLpns.Count} LPN). " +
                          $"Chuyến chuyển sang DELAYED, cập nhật ETA cho {stopChanges.Count} trạm phía trước và " +
                          $"thông báo tới {notifiedUserIds.Distinct().Count()} khách hàng."
            };

            return ApiResponse<IncidentRescueResult>.SuccessResponse(result, "Rescue vehicle dispatched successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch rescue vehicle. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentRescueResult>.Failure($"Failed to dispatch rescue vehicle: {ex.Message}");
        }
    }

    private async Task SendFirebaseRescueNotificationsAsync(
        IncidentReport incident,
        MasterTrip trip,
        Vehicle rescueVehicle,
        Guid dispatcherId,
        IReadOnlyCollection<(Guid UserId, Guid OrderId)> customerTargets)
    {
        if (_notificationService == null)
            return;

        try
        {
            foreach (var target in customerTargets.Distinct())
            {
                await _notificationService.SendToUserAsync(
                    target.UserId,
                    "Chuyến đi bị trì hoãn",
                    "Chuyến vận chuyển đã được cập nhật trạng thái trì hoãn.",
                    "TRIP_DELAYED",
                    trip.TripId.ToString(),
                    new Dictionary<string, string>
                    {
                        ["tripId"] = trip.TripId.ToString(),
                        ["incidentId"] = incident.IncidentId.ToString(),
                        ["orderId"] = target.OrderId.ToString(),
                        ["screen"] = "trip-detail"
                    });
            }

            var operationalRecipients = await _db.TripDrivers
                .Where(td => td.TripId == trip.TripId && td.Driver.UserId.HasValue)
                .Select(td => td.Driver.UserId!.Value)
                .ToListAsync();
            operationalRecipients.Add(dispatcherId);
            operationalRecipients = operationalRecipients.Distinct().ToList();

            await _notificationService.SendToUsersAsync(
                operationalRecipients,
                "Chuyến đi bị trì hoãn",
                "Chuyến vận chuyển đã được cập nhật trạng thái trì hoãn.",
                "TRIP_DELAYED",
                trip.TripId.ToString(),
                new Dictionary<string, string>
                {
                    ["tripId"] = trip.TripId.ToString(),
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["screen"] = "trip-detail"
                });

            await _notificationService.SendToUsersAsync(
                operationalRecipients,
                "Đã điều xe cứu hộ",
                "Một xe thay thế đã được phân công cho chuyến gặp sự cố.",
                "RESCUE_ASSIGNED",
                incident.IncidentId.ToString(),
                new Dictionary<string, string>
                {
                    ["tripId"] = trip.TripId.ToString(),
                    ["incidentId"] = incident.IncidentId.ToString(),
                    ["replacementVehicleId"] = rescueVehicle.VehicleId.ToString(),
                    ["screen"] = "trip-detail"
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Firebase notification dispatch failed after rescue transaction committed. IncidentId: {IncidentId}.",
                incident.IncidentId);
        }
    }


    public async Task<ApiResponse<IncidentWorkflowResult>> ConfirmTransloadAsync(
        Guid incidentId,
        ConfirmTransloadRequest request,
        Guid confirmedBy)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ConfirmationNote))
            return ApiResponse<IncidentWorkflowResult>.Failure("Vui lòng nhập ghi chú xác nhận sang hàng.");

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == confirmedBy))
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy người xác nhận.");

            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy báo cáo sự cố.");
            if (!incident.RequiresRescue)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố này không có bước sang xe.");
            if (!incident.TripId.HasValue || !incident.ReplacementVehicleId.HasValue)
                return ApiResponse<IncidentWorkflowResult>.Failure("Sự cố chưa có lệnh điều xe thay thế hợp lệ.");

            var trip = await _db.MasterTrips
                .Include(t => t.Vehicle)
                    .ThenInclude(v => v!.IotDevices)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null || trip.Vehicle == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy chuyến hoặc xe hiện tại.");
            if (trip.VehicleId != incident.ReplacementVehicleId)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Xe hiện tại của chuyến không khớp xe cứu hộ đã được điều.");

            var confirmingDriver = await _db.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == confirmedBy);
            if (confirmingDriver != null)
            {
                var isAssignedDriver = await _db.TripDrivers
                    .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == confirmingDriver.DriverId);
                if (!isAssignedDriver)
                {
                    return ApiResponse<IncidentWorkflowResult>.Failure(
                        "Bạn không phải tài xế được phân công cho chuyến này.",
                        403);
                }
            }

            if (incident.Status == "TRANSLOAD_COMPLETED" && trip.Status == "IN_TRANSIT")
            {
                return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                    BuildWorkflowResult(
                        incident,
                        trip,
                        trip.Vehicle,
                        incident.TransloadConfirmedAt ?? DbNow(),
                        "Việc sang hàng đã được xác nhận trước đó."),
                    "Việc sang hàng đã được xác nhận trước đó.");
            }

            if (incident.Status != RescueDispatchedStatus || trip.Status != "DELAYED")
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Chỉ xác nhận sang hàng khi incident ở RESCUE_DISPATCHED và trip ở DELAYED.");

            var devices = trip.Vehicle.IotDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceCode))
                .ToList();
            if (devices.Count == 0)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Xe thay thế chưa có thiết bị IoT riêng.");

            var offlineDevices = devices.Where(d => !d.IsOnline).ToList();
            if (offlineDevices.Count > 0)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Thiết bị IoT chưa online: {string.Join(", ", offlineDevices.Select(d => d.DeviceCode))}. " +
                    "Chuyến vẫn ở DELAYED.");
            }

            foreach (var device in devices)
            {
                var published = await _mqttPublisher.StartStreamingAsync(
                    device.DeviceCode!,
                    CancellationToken.None);
                if (!published)
                {
                    return ApiResponse<IncidentWorkflowResult>.Failure(
                        $"Không thể bật MQTT streaming cho thiết bị {device.DeviceCode}. Chuyến vẫn ở DELAYED.");
                }
            }

            var now = DbNow();
            trip.Status = "IN_TRANSIT";
            incident.Status = "TRANSLOAD_COMPLETED";
            incident.TransloadConfirmedBy = confirmedBy;
            incident.TransloadConfirmedAt = now;
            incident.TransloadNote = request.ConfirmationNote.Trim();

            await _db.SaveChangesAsync();

            var customerUserIds = new List<Guid>();
            var cache = new Dictionary<Guid, Guid?>();
            var customerIds = await _db.TransportOrders
                .Where(o => o.MasterTripId == trip.TripId && o.CustomerId.HasValue)
                .Select(o => o.CustomerId!.Value)
                .Distinct()
                .ToListAsync();
            foreach (var customerId in customerIds)
            {
                var userId = await ResolveCustomerUserIdAsync(customerId, cache);
                if (userId.HasValue)
                    customerUserIds.Add(userId.Value);
            }

            try
            {
                var payload = new
                {
                    incident.IncidentId,
                    trip.TripId,
                    trip.Status,
                    VehicleId = trip.Vehicle.VehicleId,
                    VehiclePlate = trip.Vehicle.TruckPlate,
                    DeviceCodes = devices.Select(d => d.DeviceCode).ToArray(),
                    incident.TransloadConfirmedAt,
                    incident.TransloadNote
                };

                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin", "Group_WarehouseWorker")
                    .SendAsync("IncidentTransloadCompleted", payload);
                foreach (var userId in customerUserIds)
                    await _hubContext.Clients.User(userId.ToString()).SendAsync("TripResumed", payload);
            }
            catch (Exception hubEx)
            {
                _logger.LogWarning(
                    hubEx,
                    "SignalR push failed after transload confirmation. IncidentId: {IncidentId}",
                    incidentId);
            }

            return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                BuildWorkflowResult(
                    incident,
                    trip,
                    trip.Vehicle,
                    now,
                    "Đã xác nhận sang toàn bộ hàng, bật MQTT streaming và cho chuyến tiếp tục."),
                "Transload confirmed and trip resumed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm incident transload. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentWorkflowResult>.Failure(
                $"Failed to confirm transload: {ex.Message}");
        }
    }


    private async Task<(string EtaMethod, List<StopEtaChange> Changes)> RecalculateEtaAsync(
        IncidentReport incident,
        List<TripStop> remainingStops,
        DateTime departFromScene)
    {
        var changes = new List<StopEtaChange>();
        if (remainingStops.Count == 0)
            return ("NO_REMAINING_STOPS", changes);

        var hasCoords = incident.CurrentLatitude.HasValue
                     && incident.CurrentLongitude.HasValue
                     && remainingStops.All(s => s.Location != null);

        string etaMethod;
        var travelSeconds = new double[remainingStops.Count];

        if (hasCoords)
        {
            var cumulativeKm = new decimal[remainingStops.Count];
            var prevLat = incident.CurrentLatitude!.Value;
            var prevLon = incident.CurrentLongitude!.Value;
            decimal cumKm = 0m;
            for (var i = 0; i < remainingStops.Count; i++)
            {
                var loc = remainingStops[i].Location!;
                cumKm += HaversineKm(prevLat, prevLon, loc.Latitude, loc.Longitude);
                cumulativeKm[i] = cumKm;
                prevLat = loc.Latitude;
                prevLon = loc.Longitude;
            }
            var totalKm = cumulativeKm[^1];

            int? goongTotalSeconds = null;
            try
            {
                var origin = GoongMapService.FormatCoordinate(incident.CurrentLatitude.Value, incident.CurrentLongitude.Value);
                var lastLoc = remainingStops[^1].Location!;
                var destination = GoongMapService.FormatCoordinate(lastLoc.Latitude, lastLoc.Longitude);
                var waypoints = string.Join("|", remainingStops
                    .Take(remainingStops.Count - 1)
                    .Select(s => GoongMapService.FormatCoordinate(s.Location!.Latitude, s.Location.Longitude)));

                var route = await _goongMapService.GetOptimizedRouteAsync(
                    origin, destination, string.IsNullOrWhiteSpace(waypoints) ? null : waypoints);
                goongTotalSeconds = route.TotalDurationSeconds;
            }
            catch (Exception goongEx)
            {
                _logger.LogWarning(goongEx, "Goong ETA recalculation failed, falling back to Haversine estimate. IncidentId: {IncidentId}", incident.IncidentId);
            }

            if (goongTotalSeconds is > 0 && totalKm > 0)
            {
                etaMethod = "GOONG";
                for (var i = 0; i < remainingStops.Count; i++)
                    travelSeconds[i] = (double)(cumulativeKm[i] / totalKm) * goongTotalSeconds.Value;
            }
            else
            {
                etaMethod = "HAVERSINE_FALLBACK";
                for (var i = 0; i < remainingStops.Count; i++)
                    travelSeconds[i] = (double)(cumulativeKm[i] / FallbackAvgSpeedKmh) * 3600d;
            }
        }
        else
        {
            etaMethod = "SHIFT_FALLBACK";
            var shift = departFromScene - remainingStops[0].PlannedArrivalTime;
            if (shift < TimeSpan.Zero) shift = TimeSpan.Zero;

            foreach (var stop in remainingStops)
            {
                var dwell = stop.PlannedDepartureTime - stop.PlannedArrivalTime;
                if (dwell < TimeSpan.Zero) dwell = TimeSpan.Zero;

                var oldEta = stop.PlannedArrivalTime;
                var newEta = oldEta + shift;

                stop.PlannedArrivalTime = newEta;
                stop.PlannedDepartureTime = newEta + dwell;

                changes.Add(BuildChange(stop, oldEta, newEta));
            }
            return (etaMethod, changes);
        }

        var cumulativeDwell = TimeSpan.Zero;
        foreach (var (stop, index) in remainingStops.Select((s, i) => (s, i)))
        {
            var dwell = stop.PlannedDepartureTime - stop.PlannedArrivalTime;
            if (dwell < TimeSpan.Zero) dwell = TimeSpan.Zero;

            var oldEta = stop.PlannedArrivalTime;
            var newEta = departFromScene.AddSeconds(travelSeconds[index]) + cumulativeDwell;
            if (newEta < oldEta) newEta = oldEta; // sự cố không thể làm hàng đến sớm hơn kế hoạch

            stop.PlannedArrivalTime = newEta;
            stop.PlannedDepartureTime = newEta + dwell;
            cumulativeDwell += dwell;

            changes.Add(BuildChange(stop, oldEta, newEta));
        }

        return (etaMethod, changes);
    }

    private static StopEtaChange BuildChange(TripStop stop, DateTime oldEta, DateTime newEta)
    {
        return new StopEtaChange
        {
            StopId = stop.StopId,
            StopSequence = stop.StopSequence,
            Address = stop.Location?.Address,
            OldEta = oldEta,
            NewEta = newEta,
            DelayMinutes = (int)Math.Max(0, (newEta - oldEta).TotalMinutes)
        };
    }


    private async Task<Guid?> ResolveCustomerUserIdAsync(Guid? customerId, Dictionary<Guid, Guid?> cache)
    {
        if (!customerId.HasValue) return null;
        if (cache.TryGetValue(customerId.Value, out var cached)) return cached;

        var customerEmail = await _db.Customers
            .Where(c => c.CustomerId == customerId.Value)
            .Select(c => c.Email)
            .FirstOrDefaultAsync();

        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(customerEmail))
        {
            userId = await _db.Users
                .Where(u => u.Email != null && u.Email.ToLower() == customerEmail.ToLower())
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        cache[customerId.Value] = userId;
        return userId;
    }

    private async Task<string?> GetOrCreateTemplateAsync(string templateId, string titleTemplate, string bodyTemplate)
    {
        var existing = await _db.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == templateId);
        if (existing != null)
        {
            existing.TitleTemplate = titleTemplate;
            existing.BodyTemplate = bodyTemplate;
            existing.Channel = "IN_APP";
            existing.Status = "ACTIVE";
            return templateId;
        }

        var msgType = await _db.Messagetypes.FirstOrDefaultAsync();
        if (msgType != null)
        {
            _db.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = templateId,
                TypeId = msgType.TypeId,
                TitleTemplate = titleTemplate,
                BodyTemplate = bodyTemplate,
                Channel = "IN_APP",
                Status = "ACTIVE"
            });
            return templateId;
        }

        return await _db.NotificationTemplates
            .Where(t => t.Status == null || t.Status == "ACTIVE")
            .Select(t => t.TemplateId)
            .FirstOrDefaultAsync();
    }

    private static string FormatCustomerEtaDate(DateTime value)
        => value.AddHours(7).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static IncidentWorkflowResult BuildWorkflowResult(
        IncidentReport incident,
        MasterTrip trip,
        Vehicle vehicle,
        DateTime confirmedAt,
        string message)
    {
        return new IncidentWorkflowResult
        {
            IncidentId = incident.IncidentId,
            IncidentStatus = incident.Status ?? "UNKNOWN",
            TripId = trip.TripId,
            TripStatus = trip.Status ?? "UNKNOWN",
            VehicleId = vehicle.VehicleId,
            VehiclePlate = vehicle.TruckPlate,
            ConfirmedAt = confirmedAt,
            Message = message
        };
    }

    private static decimal HaversineKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = ToRad((double)(lat2 - lat1));
        var dLon = ToRad((double)(lon2 - lon1));
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad((double)lat1)) * Math.Cos(ToRad((double)lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (decimal)(earthRadiusKm * c);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
