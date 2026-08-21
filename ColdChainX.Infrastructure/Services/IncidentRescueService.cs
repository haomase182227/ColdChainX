using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private readonly IIncidentWorkflowNotificationService? _workflowNotificationService;

    private static readonly string[] OnRoadTripStatuses = { "SEALED", "DISPATCHED", "IN_TRANSIT", "DELAYED" };

    private const string RescueDispatchedStatus = "RESCUE_DISPATCHED";
    private const string DelayedTemplateId = "INCIDENT_TRIP_DELAYED";
    private const string DefaultHandlingNote =
        "Tài xế xác nhận đã xử lý sự cố tại chỗ và tiếp tục hành trình.";
    private const int DefaultTransloadMinutes = 45;
    private const decimal FallbackAvgSpeedKmh = 40m;
    private const decimal NearbyColdStorageMaxDistanceKm = 100m;

    public IncidentRescueService(
        ApplicationDbContext db,
        IGoongMapService goongMapService,
        IHubContext<NotificationHub> hubContext,
        IMqttCommandPublisher mqttPublisher,
        ILogger<IncidentRescueService> logger,
        INotificationService? notificationService = null,
        ILocationService? locationService = null,
        IIncidentWorkflowNotificationService? workflowNotificationService = null)
    {
        _db = db;
        _goongMapService = goongMapService;
        _hubContext = hubContext;
        _mqttPublisher = mqttPublisher;
        _logger = logger;
        _notificationService = notificationService;
        _locationService = locationService;
        _workflowNotificationService = workflowNotificationService;
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
            if (incident.Status == "CONTAINMENT_REQUIRED")
                return ApiResponse<List<RescueCandidateResponse>>.Failure(
                    "Hãy xác nhận chống thất thoát nhiệt trước khi tìm phương án cứu hàng.");

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
                var estimatedArrivalMinutes = distanceKm.HasValue
                    ? (int?)Math.Ceiling(distanceKm.Value / FallbackAvgSpeedKmh * 60m)
                    : null;
                var canArriveWithinSafeTime = incident.TemperatureThresholdBreached
                    ? null
                    : incident.RemainingSafeTimeMinutes.HasValue && estimatedArrivalMinutes.HasValue
                        ? estimatedArrivalMinutes.Value <= incident.RemainingSafeTimeMinutes.Value
                        : (bool?)null;
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
                    EstimatedArrivalMinutes = estimatedArrivalMinutes,
                    CanArriveWithinSafeTime = canArriveWithinSafeTime,
                    RemainingSafeTimeMinutes = incident.RemainingSafeTimeMinutes,
                    RemainingWeightCapacity = v.MaxWeight - totalWeight,
                    RemainingCbmCapacity = v.MaxCbm - totalCbm,
                    TransferCount = 1,
                    RecommendationReason = incident.TemperatureThresholdBreached
                        ? "Nhiệt đã vượt ngưỡng; xe chỉ phù hợp cho phương án chuyển có kiểm soát về kho lạnh."
                        : canArriveWithinSafeTime == false
                            ? "Thời gian tiếp cận dự kiến vượt thời gian an toàn còn lại."
                            : "Đáp ứng nhiệt độ, tải/CBM và chỉ cần một lần chuyển hàng.",
                    Label = $"{v.TruckPlate} — {v.VehicleType} | tải {v.MaxWeight}kg / {v.MaxCbm}m³ | nhiệt {v.MinTemp}..{v.MaxTemp}°C | IoT {iotDeviceCount}"
                };
            })
            .OrderBy(item => item.CanArriveWithinSafeTime == true ? 0 : item.CanArriveWithinSafeTime == null ? 1 : 2)
            .ThenBy(item => item.HasOnlineIot ? 0 : 1)
            .ThenBy(item => item.TransferCount)
            .ThenBy(item => item.DistanceKm.HasValue ? 0 : 1)
            .ThenBy(item => item.DistanceKm)
            .ThenByDescending(item => item.RemainingWeightCapacity)
            .ToList();

            if (items.Count > 0)
            {
                var recommended = items.FirstOrDefault(i => i.CanArriveWithinSafeTime != false);
                if (recommended != null)
                {
                    recommended.Recommended = true;
                    recommended.RecommendationReason = incident.TemperatureThresholdBreached
                        ? "Phù hợp nhiệt độ và tải/CBM để chuyển có kiểm soát về kho lạnh; không giao trực tiếp."
                        : recommended.CanArriveWithinSafeTime == true
                            ? "Phù hợp nhiệt độ và tải/CBM, có IoT, đồng thời đến kịp thời gian an toàn còn lại."
                            : "Phù hợp nhiệt độ và tải/CBM; chưa đủ dữ liệu để so sánh với thời gian an toàn còn lại.";
                }
            }

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

    public async Task<ApiResponse<IncidentRescuePlanResponse>> GetRescuePlanAsync(Guid incidentId)
    {
        var candidatesResult = await GetRescueCandidatesAsync(incidentId);
        if (!candidatesResult.Success)
        {
            return ApiResponse<IncidentRescuePlanResponse>.Failure(
                candidatesResult.Message,
                candidatesResult.StatusCode);
        }

        try
        {
            var incident = await _db.IncidentReports.AsNoTracking()
                .FirstAsync(i => i.IncidentId == incidentId);
            var trip = await _db.MasterTrips.AsNoTracking()
                .FirstAsync(t => t.TripId == incident.TripId);
            var route = trip.RouteId.HasValue
                ? await _db.RouteMasters.AsNoTracking().FirstOrDefaultAsync(r => r.RouteId == trip.RouteId.Value)
                : null;
            var routeDestinationKey = NormalizeLocationKey(route?.DestCity);
            var warehouses = await _db.Warehouses.AsNoTracking()
                .Where(w => (w.Status == null || w.Status == "ACTIVE")
                            && (!w.DefaultMinTemp.HasValue || w.DefaultMinTemp.Value <= trip.TargetTemperature)
                            && (!w.DefaultMaxTemp.HasValue || w.DefaultMaxTemp.Value >= trip.TargetTemperature))
                .ToListAsync();
            var warehouseCoordinates = await ResolveWarehouseCoordinatesAsync(warehouses);

            var storageOptions = warehouses.Select(warehouse =>
            {
                decimal? distanceKm = null;
                if (incident.CurrentLatitude.HasValue
                    && incident.CurrentLongitude.HasValue
                    && warehouseCoordinates.TryGetValue(warehouse.WarehouseId, out var coordinates))
                {
                    distanceKm = Math.Round(HaversineKm(
                        incident.CurrentLatitude.Value,
                        incident.CurrentLongitude.Value,
                        coordinates.Latitude,
                        coordinates.Longitude), 2);
                }

                var arrivalMinutes = distanceKm.HasValue
                    ? (int?)Math.Ceiling(distanceKm.Value / FallbackAvgSpeedKmh * 60m)
                    : null;
                return new InternalColdStorageOption
                {
                    WarehouseId = warehouse.WarehouseId,
                    WarehouseName = warehouse.WarehouseName,
                    Address = warehouse.Address,
                    DistanceKm = distanceKm,
                    EstimatedArrivalMinutes = arrivalMinutes,
                    CanArriveWithinSafeTime = incident.TemperatureThresholdBreached
                        ? null
                        : incident.RemainingSafeTimeMinutes.HasValue && arrivalMinutes.HasValue
                            ? arrivalMinutes.Value <= incident.RemainingSafeTimeMinutes.Value
                            : null,
                    MinTemperature = warehouse.DefaultMinTemp,
                    MaxTemperature = warehouse.DefaultMaxTemp,
                    AvailablePalletPositions = Math.Max(0, warehouse.MaxPallets - (warehouse.CurrentPallets ?? 0)),
                    IsNearby = distanceKm.HasValue && distanceKm.Value <= NearbyColdStorageMaxDistanceKm,
                    IsRouteDestinationWarehouse = !string.IsNullOrWhiteSpace(routeDestinationKey)
                        && (NormalizeLocationKey(warehouse.WarehouseName).Contains(routeDestinationKey)
                            || NormalizeLocationKey(warehouse.WarehouseCode).Contains(routeDestinationKey)
                            || NormalizeLocationKey(warehouse.Address).Contains(routeDestinationKey))
                };
            })
            .OrderBy(w => w.CanArriveWithinSafeTime == true ? 0 : w.CanArriveWithinSafeTime == null ? 1 : 2)
            .ThenBy(w => w.DistanceKm.HasValue ? 0 : 1)
            .ThenBy(w => w.DistanceKm)
            .ToList();

            var vehicles = candidatesResult.Data ?? new List<RescueCandidateResponse>();
            var timelyVehicle = vehicles.FirstOrDefault(v => v.CanArriveWithinSafeTime != false);
            var nearbyWarehouse = storageOptions.FirstOrDefault(w => w.AvailablePalletPositions > 0
                && w.IsNearby
                && w.CanArriveWithinSafeTime != false);
            var routeDestinationWarehouse = storageOptions.FirstOrDefault(w => w.AvailablePalletPositions > 0
                && w.IsRouteDestinationWarehouse);
            var warehouseForInternalVehicle = nearbyWarehouse ?? routeDestinationWarehouse;
            var mandatoryExternalRelay = RequiresMandatoryExternalReeferRelay(incident);
            string action;
            string reason;
            if (mandatoryExternalRelay)
            {
                action = IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString();
                reason = routeDestinationWarehouse != null
                    ? "Vehicle/reefer breakdown requires an external refrigerated vehicle to carry all cargo to the route destination warehouse; a ColdChainX vehicle must then be redispatched from that warehouse for customer delivery."
                    : "Vehicle/reefer breakdown requires an external refrigerated vehicle, but no active temperature-compatible warehouse matching Route.DestCity is configured. Configure the route warehouse before dispatch.";
            }
            else if (timelyVehicle != null && !incident.DirectDeliveryLocked)
            {
                action = IncidentRescuePlanType.DIRECT_RESCUE.ToString();
                reason = "A temperature-compatible vehicle can carry the remaining load with one controlled transfer.";
            }
            else if (timelyVehicle != null && warehouseForInternalVehicle != null)
            {
                action = IncidentRescuePlanType.WAREHOUSE_RESCUE.ToString();
                reason = nearbyWarehouse != null
                    ? "Direct delivery is locked; use the compatible vehicle to reach nearby controlled cold storage."
                    : "Use the compatible internal vehicle to carry the load to the destination warehouse of the route.";
            }
            else if (timelyVehicle == null && routeDestinationWarehouse != null)
            {
                action = IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString();
                reason = "No suitable ColdChainX reefer is available near the incident; rent an external reefer to the route destination warehouse, then redispatch a ColdChainX vehicle for customer delivery.";
            }
            else if (nearbyWarehouse != null)
            {
                action = IncidentRescuePlanType.INTERNAL_COLD_STORAGE.ToString();
                reason = "No suitable vehicle can be proven timely; nearby compatible internal cold storage is the next fallback.";
            }
            else
            {
                action = IncidentRescuePlanType.MANUAL_ESCALATION.ToString();
                reason = "No valid internal option or route destination warehouse is available; keep the incident open for Dispatcher/Admin escalation. External cold storage is not part of this workflow.";
            }

            var response = new IncidentRescuePlanResponse
            {
                IncidentId = incident.IncidentId,
                TripId = trip.TripId,
                TargetTemperature = trip.TargetTemperature,
                RemainingSafeTimeMinutes = incident.RemainingSafeTimeMinutes,
                TemperatureThresholdBreached = incident.TemperatureThresholdBreached,
                DirectDeliveryLocked = incident.DirectDeliveryLocked,
                RecommendedAction = action,
                RecommendationReason = reason,
                Vehicles = vehicles,
                InternalColdStorages = storageOptions,
                RouteDestinationWarehouse = routeDestinationWarehouse,
                RequiresExternalVehicleRental = action == IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString(),
                RequiresManualEscalation = action == IncidentRescuePlanType.MANUAL_ESCALATION.ToString()
                    || (mandatoryExternalRelay && routeDestinationWarehouse == null)
            };
            return ApiResponse<IncidentRescuePlanResponse>.SuccessResponse(
                response,
                "Risk-aware rescue options retrieved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build rescue plan. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentRescuePlanResponse>.Failure($"Failed to build rescue plan: {ex.Message}");
        }
    }

    public async Task<ApiResponse<RescueFallbackResult>> RecordFallbackAsync(
        Guid incidentId,
        RecordRescueFallbackRequest request,
        Guid dispatcherId)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Note))
            return ApiResponse<RescueFallbackResult>.Failure("A fallback handling note is required.");
        if (request.PlanType is not (IncidentRescuePlanType.INTERNAL_COLD_STORAGE
            or IncidentRescuePlanType.MANUAL_ESCALATION))
        {
            return ApiResponse<RescueFallbackResult>.Failure(
                "PlanType must be INTERNAL_COLD_STORAGE or MANUAL_ESCALATION. External cold storage is not supported.");
        }

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == dispatcherId))
                return ApiResponse<RescueFallbackResult>.Failure("Dispatcher user not found.", 404);
            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<RescueFallbackResult>.Failure("Incident not found.", 404);
            if (!incident.TripId.HasValue)
                return ApiResponse<RescueFallbackResult>.Failure("Incident is not linked to a trip.");
            if (!incident.RequiresRescue)
                return ApiResponse<RescueFallbackResult>.Failure("Incident does not require a rescue fallback.");
            if (incident.Status == "CONTAINMENT_REQUIRED")
                return ApiResponse<RescueFallbackResult>.Failure("Confirm cold containment before recording a fallback.");
            if (incident.Status == "RESOLVED")
                return ApiResponse<RescueFallbackResult>.Failure("Incident is already resolved.");
            if (RequiresMandatoryExternalReeferRelay(incident))
            {
                return ApiResponse<RescueFallbackResult>.Failure(
                    "Vehicle/reefer breakdown must use external-reefer-dispatch to the route destination warehouse.");
            }

            var trip = await _db.MasterTrips.FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null)
                return ApiResponse<RescueFallbackResult>.Failure("Incident trip not found.", 404);

            string details;
            if (request.PlanType == IncidentRescuePlanType.INTERNAL_COLD_STORAGE)
            {
                if (!request.WarehouseId.HasValue)
                    return ApiResponse<RescueFallbackResult>.Failure("WarehouseId is required for internal cold storage.");
                var warehouse = await _db.Warehouses.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.WarehouseId == request.WarehouseId.Value);
                if (warehouse == null || (warehouse.Status != null && warehouse.Status != "ACTIVE"))
                    return ApiResponse<RescueFallbackResult>.Failure("Internal cold storage is not active or does not exist.");
                if ((warehouse.DefaultMinTemp.HasValue && warehouse.DefaultMinTemp.Value > trip.TargetTemperature)
                    || (warehouse.DefaultMaxTemp.HasValue && warehouse.DefaultMaxTemp.Value < trip.TargetTemperature))
                {
                    return ApiResponse<RescueFallbackResult>.Failure(
                        "Internal cold storage cannot maintain the MasterTrip target temperature.");
                }

                details = JsonSerializer.Serialize(new
                {
                    warehouse.WarehouseId,
                    warehouse.WarehouseName,
                    warehouse.Address,
                    trip.TargetTemperature,
                    Note = request.Note.Trim()
                });
            }
            else
            {
                details = JsonSerializer.Serialize(new
                {
                    RequiresDispatcherAdminDecision = true,
                    Note = request.Note.Trim()
                });
            }

            var now = DbNow();
            incident.RescuePlanType = request.PlanType.ToString();
            incident.RescuePlanDetails = details;
            incident.RedispatchPlan = request.RedispatchPlan?.Trim();
            incident.HandledBy = dispatcherId;
            incident.HandledAt = now;
            incident.HandlingNote = request.Note.Trim();
            incident.Status = request.PlanType switch
            {
                IncidentRescuePlanType.MANUAL_ESCALATION => "AWAITING_EMERGENCY_PLAN",
                _ when !string.IsNullOrWhiteSpace(request.RedispatchPlan) => "REDISPATCH_PLANNED",
                _ => "AT_INTERNAL_COLD_STORAGE"
            };
            trip.Status = "DELAYED";
            await _db.SaveChangesAsync();

            var response = new RescueFallbackResult
            {
                IncidentId = incident.IncidentId,
                TripId = trip.TripId,
                IncidentStatus = incident.Status,
                TripStatus = trip.Status ?? "DELAYED",
                PlanType = incident.RescuePlanType,
                PlanDetails = details,
                IncidentRemainsOpen = true
            };
            await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin")
                .SendAsync("IncidentFallbackRecorded", response);
            return ApiResponse<RescueFallbackResult>.SuccessResponse(
                response,
                "Cold-chain fallback recorded; the incident remains open.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record incident fallback. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<RescueFallbackResult>.Failure($"Failed to record fallback: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ExternalReeferWorkflowResult>> DispatchExternalReeferAsync(
        Guid incidentId,
        DispatchExternalReeferRequest request,
        Guid dispatcherId)
    {
        if (request == null || !request.ExternalVehicleConfirmed)
            return ApiResponse<ExternalReeferWorkflowResult>.Failure("ExternalVehicleConfirmed must be true.");
        if ((request.EvidenceUrls ?? new List<string>()).Any(url => !IsValidEvidenceUrl(url)))
            return ApiResponse<ExternalReeferWorkflowResult>.Failure("EvidenceUrls must contain valid HTTP/HTTPS URLs.");

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == dispatcherId))
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Dispatcher user not found.", 404);

            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Incident not found.", 404);
            if (!incident.RequiresRescue || !incident.TripId.HasValue)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Incident is not eligible for rescue transport.");
            if (incident.Status == "CONTAINMENT_REQUIRED")
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Confirm cold containment before renting an external reefer.");
            if (incident.Status is "RESOLVED" or "EXTERNAL_REEFER_IN_TRANSIT" or "READY_FOR_REDISPATCH" or "REDISPATCH_PLANNED" or "REDISPATCHED_TO_CUSTOMER")
                return ApiResponse<ExternalReeferWorkflowResult>.Failure($"Incident status {incident.Status} does not allow another external dispatch.");

            var trip = await _db.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.Route)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            if (trip == null || trip.Route == null || trip.Vehicle == null)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Trip, route or current vehicle is missing.");
            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Trip is not in an on-road status.");

            var activeWarehouses = await _db.Warehouses
                .Where(w => (w.Status == null || w.Status == "ACTIVE")
                    && (!w.DefaultMinTemp.HasValue || w.DefaultMinTemp.Value <= trip.TargetTemperature)
                    && (!w.DefaultMaxTemp.HasValue || w.DefaultMaxTemp.Value >= trip.TargetTemperature))
                .ToListAsync();
            var warehouse = request.DestinationWarehouseId.HasValue
                && request.DestinationWarehouseId.Value != Guid.Empty
                    ? activeWarehouses.FirstOrDefault(w => w.WarehouseId == request.DestinationWarehouseId.Value)
                    : activeWarehouses.FirstOrDefault(w => MatchesRouteDestination(w, trip.Route));
            if (warehouse == null)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure(
                    "No active temperature-compatible warehouse matching the route destination is configured.");
            if (!MatchesRouteDestination(warehouse, trip.Route))
            {
                return ApiResponse<ExternalReeferWorkflowResult>.Failure(
                    $"Warehouse {warehouse.WarehouseName} does not match route destination {trip.Route.DestCity}.");
            }
            if ((warehouse.DefaultMinTemp.HasValue && warehouse.DefaultMinTemp.Value > trip.TargetTemperature)
                || (warehouse.DefaultMaxTemp.HasValue && warehouse.DefaultMaxTemp.Value < trip.TargetTemperature))
            {
                return ApiResponse<ExternalReeferWorkflowResult>.Failure(
                    "Route destination warehouse cannot maintain the MasterTrip target temperature.");
            }
            if (request.AgreedTemperature.HasValue
                && Math.Abs(request.AgreedTemperature.Value - trip.TargetTemperature) > incident.TemperatureTolerance)
            {
                return ApiResponse<ExternalReeferWorkflowResult>.Failure(
                    "External reefer agreed temperature is outside the MasterTrip target tolerance.");
            }

            var shippingLpns = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .ToListAsync();
            var requestedLpnIds = request.LpnIds ?? new List<Guid>();
            var selectedLpnIds = requestedLpnIds.Count > 0
                ? requestedLpnIds.Distinct().ToList()
                : shippingLpns.Select(l => l.LpnId).ToList();
            if (!ContainsExactly(selectedLpnIds, shippingLpns.Select(l => l.LpnId)))
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("External reefer handover must include every SHIPPING LPN on the trip.");

            var now = DbNow();
            var evidenceUrls = (request.EvidenceUrls ?? new List<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var plan = new ExternalReeferPlanRecord
            {
                RentalProvider = NormalizeExternalVehicleText(request.RentalProvider, "Đối tác xe lạnh ngoài"),
                VehiclePlate = NormalizeExternalVehicleText(request.VehiclePlate, "XE LẠNH NGOÀI").ToUpperInvariant(),
                DriverName = NormalizeExternalVehicleText(request.DriverName, "Tài xế đối tác"),
                DriverPhone = request.DriverPhone?.Trim(),
                DestinationWarehouseId = warehouse.WarehouseId,
                DestinationWarehouseName = warehouse.WarehouseName,
                DestinationWarehouseAddress = warehouse.Address,
                RouteDestinationCity = trip.Route.DestCity,
                AgreedTemperature = request.AgreedTemperature ?? trip.TargetTemperature,
                OriginalTripId = trip.TripId,
                DispatchedAt = now,
                ExpectedWarehouseArrivalAt = request.ExpectedWarehouseArrivalAt.HasValue
                    ? DateTime.SpecifyKind(request.ExpectedWarehouseArrivalAt.Value, DateTimeKind.Unspecified)
                    : null,
                SealNumber = request.SealNumber?.Trim() ?? string.Empty,
                LpnIds = selectedLpnIds,
                DispatchEvidenceUrls = evidenceUrls,
                RecordedBy = dispatcherId,
                DispatchNote = NormalizeExternalVehicleText(
                    request.Note,
                    "Dispatcher xác nhận đã có xe lạnh ngoài; chuyển task cho kho đích inbound cứu hộ bằng seal.")
            };

            var brokenVehicle = trip.Vehicle;
            brokenVehicle.Status = "MAINTENANCE";
            if (incident.CurrentLatitude.HasValue && incident.CurrentLongitude.HasValue)
            {
                brokenVehicle.CurrentLocation = GoongMapService.FormatCoordinate(
                    incident.CurrentLatitude.Value,
                    incident.CurrentLongitude.Value);
            }
            if (!incident.MaintenanceTicketId.HasValue)
            {
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
                incident.MaintenanceTicketId = ticket.TicketId;
            }

            incident.BrokenVehicleId ??= brokenVehicle.VehicleId;
            incident.ReplacementVehicleId = null;
            incident.RescuePlanType = IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString();
            incident.RescuePlanDetails = JsonSerializer.Serialize(plan);
            incident.RedispatchPlan = $"Khi hàng đến {warehouse.WarehouseName}, Dispatcher chọn xe ColdChainX tại kho để giao khách.";
            incident.Status = "EXTERNAL_REEFER_IN_TRANSIT";
            incident.HandledBy = dispatcherId;
            incident.HandledAt = now;
            incident.HandlingNote = plan.DispatchNote;
            incident.RescueDispatchedAt = now;
            trip.Status = "DELAYED";
            foreach (var evidenceUrl in evidenceUrls)
            {
                _db.IncidentEvidences.Add(new IncidentEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incident.IncidentId,
                    EvidenceType = "EXTERNAL_REEFER_HANDOVER",
                    FileUrl = evidenceUrl
                });
            }

            await _db.SaveChangesAsync();
            var result = BuildExternalReeferResult(
                incident,
                trip,
                plan,
                $"Đã xác nhận có xe lạnh ngoài; {warehouse.WarehouseName} có thể thực hiện inbound cứu hộ bằng seal.");
            if (_workflowNotificationService != null)
            {
                await NotifyExternalReeferAudiencesAsync(
                    incident,
                    trip,
                    warehouse,
                    plan,
                    dispatcherId,
                    result);
            }
            else
            {
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin", "Group_WarehouseWorker")
                    .SendAsync("ExternalReeferDispatched", result);
            }
            return ApiResponse<ExternalReeferWorkflowResult>.SuccessResponse(
                result,
                "External vehicle confirmed; route warehouse emergency inbound is ready.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch external reefer. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<ExternalReeferWorkflowResult>.Failure($"Failed to dispatch external reefer: {ex.Message}");
        }
    }

    public async Task<ApiResponse<ExternalReeferWorkflowResult>> InboundRouteWarehouseAsync(
        Guid incidentId,
        InboundRouteWarehouseRequest request,
        Guid confirmedBy)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SealNumber))
            return ApiResponse<ExternalReeferWorkflowResult>.Failure("SealNumber is required.");

        try
        {
            if (!await _db.Users.AnyAsync(u => u.UserId == confirmedBy))
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Confirming user not found.", 404);
            var incident = await _db.IncidentReports.FirstOrDefaultAsync(i => i.IncidentId == incidentId);
            if (incident == null || !incident.TripId.HasValue)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Incident or trip not found.", 404);
            if (incident.Status != "EXTERNAL_REEFER_IN_TRANSIT"
                || incident.RescuePlanType != IncidentRescuePlanType.EXTERNAL_REEFER_TO_ROUTE_WAREHOUSE.ToString())
            {
                return ApiResponse<ExternalReeferWorkflowResult>.Failure(
                    "Only an EXTERNAL_REEFER_IN_TRANSIT incident can be received at the route warehouse.");
            }

            var plan = DeserializeExternalReeferPlan(incident.RescuePlanDetails);
            if (plan == null)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("External reefer plan details are missing or invalid.");
            var trip = await _db.MasterTrips
                .Include(t => t.TripStops)
                .Include(t => t.TripDrivers)
                    .ThenInclude(td => td.Driver)
                .FirstOrDefaultAsync(t => t.TripId == incident.TripId.Value);
            var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == plan.DestinationWarehouseId);
            if (trip == null || warehouse == null)
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Trip or route destination warehouse not found.");
            var inboundSeal = request.SealNumber.Trim();
            if (!string.IsNullOrWhiteSpace(plan.SealNumber)
                && !string.Equals(plan.SealNumber, inboundSeal, StringComparison.OrdinalIgnoreCase))
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Arrival seal does not match the external reefer handover seal.");
            if (string.IsNullOrWhiteSpace(plan.SealNumber))
                plan.SealNumber = inboundSeal;

            var lpns = await _db.Lpns
                .Include(l => l.Order)
                .Where(l => plan.LpnIds.Contains(l.LpnId))
                .ToListAsync();
            if (lpns.Count != plan.LpnIds.Distinct().Count())
                return ApiResponse<ExternalReeferWorkflowResult>.Failure("Not all LPNs handed to the external reefer could be found.");

            var now = DbNow();
            var inboundReceiptIds = new List<Guid>();
            foreach (var orderGroup in lpns.GroupBy(l => l.OrderId))
            {
                var order = orderGroup.First().Order;
                var receipt = new WarehouseReceipt
                {
                    ReceiptId = Guid.NewGuid(),
                    ReceiptCode = $"INC-IN-{incident.IncidentId.ToString("N")[..8]}-{orderGroup.Key.ToString("N")[..8]}",
                    ReferenceDocNo = incident.IncidentId.ToString(),
                    OrderId = orderGroup.Key,
                    WarehouseId = warehouse.WarehouseId,
                    ReceiptType = "INCIDENT_RELAY_INBOUND",
                    Reason = incident.IncidentType,
                    TotalExpectedQty = orderGroup.Sum(l => l.Quantity),
                    TotalActualQty = orderGroup.Sum(l => l.Quantity),
                    DelivererName = $"{plan.RentalProvider} - {plan.DriverName}",
                    ReceiverId = confirmedBy,
                    Note = $"Inbound bằng seal {request.SealNumber.Trim()}, bỏ qua QC theo luồng cứu hộ sự cố.",
                    CreatedAt = now
                };
                _db.WarehouseReceipts.Add(receipt);
                inboundReceiptIds.Add(receipt.ReceiptId);

                foreach (var lpn in orderGroup)
                {
                    lpn.ReceiptId = receipt.ReceiptId;
                    lpn.WarehouseId = warehouse.WarehouseId;
                    lpn.TripId = null;
                    lpn.State = LpnState.IN_STOCK;
                    lpn.InboundTime = now;
                    lpn.UpdatedAt = now;
                }

                order.MasterTripId = null;
                order.Status = "READY_FOR_ROUTING";
            }
            plan.ArrivedAt = now;
            plan.ArrivalConfirmedBy = confirmedBy;
            plan.InboundReceiptIds = inboundReceiptIds;
            plan.ArrivalNote = $"Warehouse Worker đã inbound bằng seal {request.SealNumber.Trim()}, không qua QC.";
            incident.RescuePlanDetails = JsonSerializer.Serialize(plan);
            incident.Status = "READY_FOR_REDISPATCH";
            incident.HandledBy = confirmedBy;
            incident.HandledAt = now;
            incident.RedispatchPlan = $"Chờ Dispatcher ghép chuyến mới từ {warehouse.WarehouseName} bằng manual-dispatch.";
            trip.Status = "RELAY_COMPLETED";
            foreach (var stop in trip.TripStops.Where(s => s.Status is not ("COMPLETED" or "ARRIVED" or "CANCELLED")))
                stop.Status = "CANCELLED";
            foreach (var tripDriver in trip.TripDrivers)
            {
                if (tripDriver.Driver?.Status is "ONTRIP" or "ON_TRIP" or "PLANNING")
                    tripDriver.Driver.Status = "ACTIVE";
            }

            var coordinates = await ResolveWarehouseCoordinatesAsync(new[] { warehouse });
            if (coordinates.TryGetValue(warehouse.WarehouseId, out var position))
            {
                incident.CurrentLatitude = position.Latitude;
                incident.CurrentLongitude = position.Longitude;
            }
            await _db.SaveChangesAsync();
            var result = BuildExternalReeferResult(
                incident,
                trip,
                plan,
                $"Đã inbound {lpns.Count} LPN tại {warehouse.WarehouseName} bằng seal; chờ Dispatcher ghép chuyến mới.");
            if (_workflowNotificationService != null)
            {
                await NotifyRouteWarehouseInboundAudiencesAsync(
                    incident,
                    trip,
                    warehouse,
                    plan,
                    confirmedBy,
                    lpns.Select(lpn => lpn.LpnCode).ToList(),
                    result);
            }
            else
            {
                await _hubContext.Clients.Groups("Group_Dispatcher", "Group_Admin", "Group_WarehouseWorker")
                    .SendAsync("IncidentCargoInboundedAtRouteWarehouse", result);
            }
            return ApiResponse<ExternalReeferWorkflowResult>.SuccessResponse(result, "Cargo inbounded at route warehouse without QC.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inbound cargo at route warehouse. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<ExternalReeferWorkflowResult>.Failure($"Failed to inbound cargo at route warehouse: {ex.Message}");
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

    private static bool MatchesRouteDestination(Warehouse warehouse, RouteMaster route)
    {
        var destinationKey = NormalizeLocationKey(route.DestCity);
        return !string.IsNullOrWhiteSpace(destinationKey)
            && (NormalizeLocationKey(warehouse.WarehouseName).Contains(destinationKey)
                || NormalizeLocationKey(warehouse.WarehouseCode).Contains(destinationKey)
                || NormalizeLocationKey(warehouse.Address).Contains(destinationKey));
    }

    private static bool RequiresMandatoryExternalReeferRelay(IncidentReport incident)
        => incident.IncidentType.Equals(IncidentType.VEHICLE_BREAKDOWN.ToString(), StringComparison.OrdinalIgnoreCase)
            || incident.IncidentType.Equals(IncidentType.REEFER_BREAKDOWN.ToString(), StringComparison.OrdinalIgnoreCase)
            || incident.IncidentType.Equals("BREAKDOWN", StringComparison.OrdinalIgnoreCase)
            || incident.IncidentType.Equals("COOLING_FAILURE", StringComparison.OrdinalIgnoreCase)
            || incident.IncidentType.Equals("REFRIGERATION_BREAKDOWN", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExactly(IEnumerable<Guid> actual, IEnumerable<Guid> expected)
        => actual.Distinct().ToHashSet().SetEquals(expected.Distinct());

    private static ExternalReeferPlanRecord? DeserializeExternalReeferPlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ExternalReeferPlanRecord>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExternalReeferWorkflowResult BuildExternalReeferResult(
        IncidentReport incident,
        MasterTrip trip,
        ExternalReeferPlanRecord plan,
        string message)
        => new()
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            IncidentStatus = incident.Status ?? "UNKNOWN",
            TripStatus = trip.Status ?? "UNKNOWN",
            DestinationWarehouseId = plan.DestinationWarehouseId,
            DestinationWarehouseName = plan.DestinationWarehouseName,
            ExternalVehiclePlate = plan.VehiclePlate,
            LpnCount = plan.LpnIds.Count,
            WarehouseInboundReady = incident.Status == "EXTERNAL_REEFER_IN_TRANSIT",
            RequiredWarehouseAction = incident.Status == "EXTERNAL_REEFER_IN_TRANSIT"
                ? "INBOUND_RESCUE_BY_SEAL"
                : "CREATE_REDISPATCH_TRIP",
            Message = message
        };

    private static string NormalizeExternalVehicleText(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeLocationKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character == 'Đ' ? 'D' : character);
        }

        return string.Concat(builder.ToString().Normalize(NormalizationForm.FormC).Where(char.IsLetterOrDigit));
    }


    public async Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(
        Guid incidentId,
        ContinueTripAfterIncidentRequest request,
        Guid actorUserId)
    {
        var handlingNote = string.IsNullOrWhiteSpace(request.HandlingNote)
            ? DefaultHandlingNote
            : request.HandlingNote.Trim();

        try
        {
            var actor = await _db.Users
                .Include(u => u.Role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == actorUserId);
            if (actor == null)
                return ApiResponse<IncidentWorkflowResult>.Failure("Không tìm thấy người xử lý.", 404);

            var driver = await _db.Drivers
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.UserId == actorUserId);
            var isDispatcher = actor.Role?.RoleName.Equals("Dispatcher", StringComparison.OrdinalIgnoreCase) == true
                               || actor.Role?.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;
            if (driver == null && !isDispatcher)
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Chỉ tài xế được phân công hoặc Dispatcher mới có thể cho chuyến tiếp tục.",
                    403);

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
            if (incident.RiskLevel == IncidentRiskLevel.CRITICAL.ToString()
                || incident.TemperatureThresholdBreached
                || incident.DirectDeliveryLocked)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Không thể tiếp tục trực tiếp khi incident đang CRITICAL hoặc giao trực tiếp đang bị khóa.");
            }

            var trip = incident.Trip;
            if (driver != null)
            {
                var isAssignedDriver = await _db.TripDrivers
                    .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId);
                if (!isAssignedDriver)
                {
                    return ApiResponse<IncidentWorkflowResult>.Failure(
                        "Bạn không phải tài xế được phân công cho chuyến này.",
                        403);
                }
            }

            if (incident.Status == "CONTINUED" && trip.Status == "IN_TRANSIT")
            {
                return ApiResponse<IncidentWorkflowResult>.SuccessResponse(
                    BuildWorkflowResult(incident, trip, trip.Vehicle, incident.HandledAt ?? DbNow(),
                        "Chuyến đã được cho tiếp tục trước đó."),
                    "Trip already continued.");
            }

            if (incident.Status is not ("REPORTED" or "TRIAGED" or "MONITORING"))
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Sự cố đang ở trạng thái {incident.Status ?? "UNKNOWN"} và không thể tiếp tục theo nhánh tự xử lý.");
            }

            if (!OnRoadTripStatuses.Contains(trip.Status))
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"Chuyến đang ở trạng thái {trip.Status ?? "UNKNOWN"} và không thể tiếp tục từ luồng sự cố.");

            var now = DbNow();
            if (request.ExpectedDelayMinutes < 0)
                return ApiResponse<IncidentWorkflowResult>.Failure("ExpectedDelayMinutes cannot be negative.");
            if (request.ExpectedDelayMinutes > 0)
            {
                var delay = TimeSpan.FromMinutes(request.ExpectedDelayMinutes);
                var remainingStops = await _db.TripStops
                    .Where(s => s.TripId == trip.TripId
                                && s.ActualArrivalTime == null
                                && s.Status != "COMPLETED"
                                && s.Status != "CANCELLED")
                    .ToListAsync();
                foreach (var stop in remainingStops)
                {
                    stop.PlannedArrivalTime += delay;
                    stop.PlannedDepartureTime += delay;
                    stop.Status = "DELAYED_INCIDENT";
                }
                trip.PlannedEndTime += delay;
            }
            trip.Status = "IN_TRANSIT";
            incident.Status = "CONTINUED";
            incident.HandledBy = actorUserId;
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
        if (request.PlanType is not (IncidentRescuePlanType.DIRECT_RESCUE or IncidentRescuePlanType.WAREHOUSE_RESCUE))
            return ApiResponse<IncidentRescueResult>.Failure("PlanType must be DIRECT_RESCUE or WAREHOUSE_RESCUE.");

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
            if (incident.Status == "CONTAINMENT_REQUIRED")
                return ApiResponse<IncidentRescueResult>.Failure(
                    "Hãy xác nhận chống thất thoát nhiệt trước khi điều xe cứu hàng.");
            if (RequiresMandatoryExternalReeferRelay(incident))
            {
                return ApiResponse<IncidentRescueResult>.Failure(
                    "Sự cố xe/thùng lạnh bắt buộc thuê xe lạnh ngoài chở về kho đích tuyến; " +
                    "hãy dùng external-reefer-dispatch, inbound-route-warehouse, sau đó ghép chuyến mới bằng manual-dispatch.");
            }
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
            if (request.PlanType == IncidentRescuePlanType.DIRECT_RESCUE && incident.DirectDeliveryLocked)
            {
                return ApiResponse<IncidentRescueResult>.Failure(
                    "Giao trực tiếp đang bị khóa vì nhiệt độ đã vượt ngưỡng; hãy chọn WAREHOUSE_RESCUE.");
            }

            Warehouse? destinationWarehouse = null;
            if (request.PlanType == IncidentRescuePlanType.WAREHOUSE_RESCUE)
            {
                if (!request.DestinationWarehouseId.HasValue)
                    return ApiResponse<IncidentRescueResult>.Failure("DestinationWarehouseId is required for WAREHOUSE_RESCUE.");
                destinationWarehouse = await _db.Warehouses.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.WarehouseId == request.DestinationWarehouseId.Value);
                if (destinationWarehouse == null || (destinationWarehouse.Status != null && destinationWarehouse.Status != "ACTIVE"))
                    return ApiResponse<IncidentRescueResult>.Failure("Destination cold storage is not active or does not exist.");
                if ((destinationWarehouse.DefaultMinTemp.HasValue && destinationWarehouse.DefaultMinTemp > trip.TargetTemperature)
                    || (destinationWarehouse.DefaultMaxTemp.HasValue && destinationWarehouse.DefaultMaxTemp < trip.TargetTemperature))
                {
                    return ApiResponse<IncidentRescueResult>.Failure(
                        "Destination cold storage cannot maintain the MasterTrip target temperature.");
                }
            }

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

            var estimatedArrivalMinutes = await EstimateVehicleArrivalMinutesAsync(rescueVehicle, incident);
            if (!incident.TemperatureThresholdBreached
                && incident.RemainingSafeTimeMinutes.HasValue
                && estimatedArrivalMinutes.HasValue
                && estimatedArrivalMinutes.Value > incident.RemainingSafeTimeMinutes.Value)
            {
                return ApiResponse<IncidentRescueResult>.Failure(
                    $"Xe {rescueVehicle.TruckPlate} dự kiến tiếp cận sau {estimatedArrivalMinutes} phút, " +
                    $"vượt thời gian an toàn còn lại {incident.RemainingSafeTimeMinutes} phút. Hãy dùng phương án bảo quản lạnh fallback.");
            }

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
            incident.RescuePlanType = request.PlanType.ToString();
            incident.RescuePlanDetails = JsonSerializer.Serialize(new
            {
                request.PlanType,
                request.ReplacementVehicleId,
                request.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse?.WarehouseName,
                EstimatedArrivalMinutes = estimatedArrivalMinutes,
                incident.RemainingSafeTimeMinutes,
                Note = request.Note?.Trim()
            });

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
        if (request.Latitude is < -90m or > 90m || request.Longitude is < -180m or > 180m)
            return ApiResponse<IncidentWorkflowResult>.Failure("Transload coordinates are invalid.");
        if ((request.EvidenceUrls ?? new List<string>()).Any(url => !IsValidEvidenceUrl(url)))
            return ApiResponse<IncidentWorkflowResult>.Failure("EvidenceUrls must contain valid HTTP/HTTPS URLs.");

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

            if (incident.TransloadConfirmedAt.HasValue && trip.Status == "IN_TRANSIT")
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

            var wasResolvedAfterReplacementDispatch = incident.Status == "RESOLVED"
                && incident.ResolvedAt.HasValue
                && incident.ReplacementVehicleId.HasValue
                && incident.RescueDispatchedAt.HasValue;
            if ((incident.Status != RescueDispatchedStatus && !wasResolvedAfterReplacementDispatch)
                || trip.Status != "DELAYED")
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "Chỉ xác nhận sang hàng sau khi xe thay thế đã được điều và trip đang DELAYED.");

            var shippingLpns = await _db.Lpns
                .Where(l => l.TripId == trip.TripId && l.State == LpnState.SHIPPING)
                .ToListAsync();
            var requestedLpnIds = request.LpnIds ?? new List<Guid>();
            var selectedLpnIds = requestedLpnIds.Count > 0
                ? requestedLpnIds.Distinct().ToList()
                : shippingLpns.Select(l => l.LpnId).ToList();
            var shippingLpnIds = shippingLpns.Select(l => l.LpnId).ToHashSet();
            var invalidLpnIds = selectedLpnIds.Where(id => !shippingLpnIds.Contains(id)).ToList();
            if (invalidLpnIds.Count > 0)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    $"LPNs are not shipping on this trip: {string.Join(", ", invalidLpnIds)}.");
            }
            if (selectedLpnIds.Count != shippingLpnIds.Count)
            {
                return ApiResponse<IncidentWorkflowResult>.Failure(
                    "This short-term workflow requires confirming every remaining SHIPPING LPN in the controlled transload.");
            }

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
            var transferTemperature = request.TransferTemperature;
            if (!transferTemperature.HasValue)
            {
                transferTemperature = await _db.TelemetryLogs
                    .AsNoTracking()
                    .Where(t => t.TripId == trip.TripId)
                    .OrderByDescending(t => t.Timestamp)
                    .Select(t => (decimal?)t.Temperature)
                    .FirstOrDefaultAsync();
            }
            var transferredAt = request.TransferredAt.HasValue
                ? DateTime.SpecifyKind(request.TransferredAt.Value, DateTimeKind.Unspecified)
                : now;
            var evidenceUrls = (request.EvidenceUrls ?? new List<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var transloadRecord = new TransloadRecord
            {
                LpnIds = selectedLpnIds,
                SealNumber = request.SealNumber?.Trim(),
                TransferTemperature = transferTemperature,
                TransferredAt = transferredAt,
                Latitude = request.Latitude ?? incident.CurrentLatitude,
                Longitude = request.Longitude ?? incident.CurrentLongitude,
                LocationDescription = request.LocationDescription?.Trim(),
                EvidenceUrls = evidenceUrls,
                ConfirmedBy = confirmedBy
            };
            trip.Status = "IN_TRANSIT";
            if (!wasResolvedAfterReplacementDispatch)
                incident.Status = "TRANSLOAD_COMPLETED";
            incident.TransloadConfirmedBy = confirmedBy;
            incident.TransloadConfirmedAt = now;
            incident.TransloadNote = request.ConfirmationNote.Trim();
            incident.TransloadDetailsJson = JsonSerializer.Serialize(transloadRecord);
            foreach (var evidenceUrl in evidenceUrls)
            {
                _db.IncidentEvidences.Add(new IncidentEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    IncidentId = incident.IncidentId,
                    EvidenceType = "TRANSLOAD_EVIDENCE",
                    FileUrl = evidenceUrl
                });
            }

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
                    incident.TransloadNote,
                    Transload = transloadRecord
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
                    $"Đã xác nhận sang {selectedLpnIds.Count} LPN có kiểm soát, bật MQTT streaming và cho chuyến tiếp tục."),
                "Transload confirmed and trip resumed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm incident transload. IncidentId: {IncidentId}", incidentId);
            return ApiResponse<IncidentWorkflowResult>.Failure(
                $"Failed to confirm transload: {ex.Message}");
        }
    }

    private async Task<int?> EstimateVehicleArrivalMinutesAsync(Vehicle vehicle, IncidentReport incident)
    {
        if (!incident.CurrentLatitude.HasValue || !incident.CurrentLongitude.HasValue)
            return null;

        var warehouseId = ParseWarehouseId(vehicle.CurrentLocation);
        if (!warehouseId.HasValue)
            return null;
        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId.Value);
        if (warehouse == null)
            return null;
        var coordinates = await ResolveWarehouseCoordinatesAsync(new[] { warehouse });
        if (!coordinates.TryGetValue(warehouse.WarehouseId, out var position))
            return null;

        var distance = HaversineKm(
            incident.CurrentLatitude.Value,
            incident.CurrentLongitude.Value,
            position.Latitude,
            position.Longitude);
        return (int)Math.Ceiling(distance / FallbackAvgSpeedKmh * 60m);
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
                    origin, destination, string.IsNullOrWhiteSpace(waypoints) ? null : waypoints, optimize: false);
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


    private async Task NotifyExternalReeferAudiencesAsync(
        IncidentReport incident,
        MasterTrip trip,
        Warehouse warehouse,
        ExternalReeferPlanRecord plan,
        Guid dispatcherId,
        ExternalReeferWorkflowResult payload)
    {
        if (_workflowNotificationService == null)
            return;

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "EXTERNAL_REEFER_DISPATCHED",
            Title = "Đã xác nhận có xe lạnh ngoài",
            Body = $"Incident đã chuyển sang chờ {warehouse.WarehouseName} inbound cứu hộ bằng seal.",
            RecipientRoles = new[] { "ADMIN", "DISPATCHER" },
            AdditionalUserIds = new[] { dispatcherId },
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher" },
            RealtimeEventName = "ExternalReeferDispatched",
            Payload = payload
        });

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "EMERGENCY_INBOUND_PREPARE",
            Title = "Có hàng cứu hộ cần inbound khẩn cấp",
            Body = string.IsNullOrWhiteSpace(plan.SealNumber)
                ? $"{warehouse.WarehouseName} có thể mở task inbound ngay; khi hàng đến chỉ nhập seal, không QC."
                : $"{warehouse.WarehouseName} có thể mở task inbound ngay bằng seal {plan.SealNumber}; không QC.",
            RecipientRoles = new[] { "WAREHOUSEWORKER" },
            RecipientWarehouseId = warehouse.WarehouseId,
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeEventName = "WarehouseEmergencyInboundRequested",
            Payload = new
            {
                incident.IncidentId,
                trip.TripId,
                warehouse.WarehouseId,
                warehouse.WarehouseName,
                plan.VehiclePlate,
                plan.SealNumber,
                LpnCount = plan.LpnIds.Count,
                plan.ExpectedWarehouseArrivalAt
            }
        });

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "RESCUE_VEHICLE_SENT",
            Title = "Đã xác nhận có xe cứu hộ",
            Body = $"Đã có xe lạnh ngoài tiếp nhận hàng và đưa về {warehouse.WarehouseName}.",
            IncludeReporter = false,
            IncludeTripDrivers = true,
            RealtimeEventName = "DriverRescueVehicleDispatched",
            Payload = new
            {
                incident.IncidentId,
                trip.TripId,
                plan.VehiclePlate,
                plan.DriverName,
                plan.DriverPhone,
                warehouse.WarehouseName,
                plan.ExpectedWarehouseArrivalAt
            }
        });

        var orderTargets = await _db.Lpns
            .AsNoTracking()
            .Where(lpn => plan.LpnIds.Contains(lpn.LpnId))
            .Select(lpn => new
            {
                lpn.OrderId,
                lpn.Order.TrackingCode,
                lpn.Order.CustomerId
            })
            .Distinct()
            .ToListAsync();
        var customerUserCache = new Dictionary<Guid, Guid?>();
        foreach (var order in orderTargets)
        {
            var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId, customerUserCache);
            if (!customerUserId.HasValue)
                continue;

            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = incident.IncidentId,
                TripId = trip.TripId,
                Action = "CUSTOMER_ORDER_DELAYED_VEHICLE_CHANGE",
                Title = $"Đơn {order.TrackingCode} có thể giao trễ",
                Body = "Xe vận chuyển gặp sự cố nên ColdChainX đang đổi sang xe lạnh khác. Hàng được bảo quản lạnh và lịch giao sẽ được cập nhật sớm.",
                AdditionalUserIds = new[] { customerUserId.Value },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeEventName = "CustomerOrderDelayedByVehicleChange",
                NotificationType = "ORDER_DELAYED",
                ReferenceId = order.OrderId.ToString(),
                Screen = "ORDER_DETAIL",
                AdditionalData = new Dictionary<string, string>
                {
                    ["orderId"] = order.OrderId.ToString(),
                    ["trackingCode"] = order.TrackingCode
                },
                Payload = new
                {
                    incident.IncidentId,
                    trip.TripId,
                    order.OrderId,
                    order.TrackingCode,
                    Reason = "VEHICLE_CHANGE"
                }
            });
        }
    }

    private async Task NotifyRouteWarehouseInboundAudiencesAsync(
        IncidentReport incident,
        MasterTrip trip,
        Warehouse warehouse,
        ExternalReeferPlanRecord plan,
        Guid confirmedBy,
        IReadOnlyCollection<string> lpnCodes,
        ExternalReeferWorkflowResult payload)
    {
        if (_workflowNotificationService == null)
            return;

        var lpnSummary = FormatLpnCodes(lpnCodes);
        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "URGENT_REDISPATCH_REQUIRED",
            Title = "Cần tạo lại chuyến giao hàng gấp",
            Body = $"Hàng đã vào {warehouse.WarehouseName}. Hãy tạo trip mới cho {lpnCodes.Count} LPN: {lpnSummary}.",
            RecipientRoles = new[] { "ADMIN", "DISPATCHER" },
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeGroups = new[] { "Group_Admin", "Group_Dispatcher" },
            RealtimeEventName = "IncidentCargoInboundedAtRouteWarehouse",
            Payload = new
            {
                Incident = payload,
                LpnCodes = lpnCodes,
                RequiredAction = "CREATE_REDISPATCH_TRIP",
                Priority = "URGENT"
            }
        });

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "ROUTE_WAREHOUSE_INBOUNDED",
            Title = "Đã nhập hàng sự cố khẩn cấp",
            Body = $"Đã đối chiếu seal {plan.SealNumber} và nhập {lpnCodes.Count} LPN vào {warehouse.WarehouseName}; không qua QC.",
            RecipientRoles = new[] { "WAREHOUSEWORKER" },
            RecipientWarehouseId = warehouse.WarehouseId,
            AdditionalUserIds = new[] { confirmedBy },
            IncludeReporter = false,
            IncludeTripDrivers = false,
            RealtimeEventName = "IncidentCargoInboundedAtRouteWarehouse",
            Payload = payload
        });

        await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
        {
            IncidentId = incident.IncidentId,
            TripId = trip.TripId,
            Action = "RESCUE_VEHICLE_ARRIVED_WAREHOUSE",
            Title = "Xe cứu hộ đã đến kho",
            Body = $"Hàng đã được xe {plan.VehiclePlate} đưa đến {warehouse.WarehouseName} và bàn giao đủ seal.",
            IncludeReporter = false,
            IncludeTripDrivers = true,
            RealtimeEventName = "DriverRescueArrivedAtWarehouse",
            Payload = payload
        });

        var orderTargets = await _db.Lpns
            .AsNoTracking()
            .Where(lpn => plan.LpnIds.Contains(lpn.LpnId))
            .Select(lpn => new { lpn.OrderId, lpn.Order.TrackingCode, lpn.Order.CustomerId })
            .Distinct()
            .ToListAsync();
        var customerUserCache = new Dictionary<Guid, Guid?>();
        foreach (var order in orderTargets)
        {
            var customerUserId = await ResolveCustomerUserIdAsync(order.CustomerId, customerUserCache);
            if (!customerUserId.HasValue)
                continue;

            await _workflowNotificationService.NotifyAsync(new IncidentWorkflowNotification
            {
                IncidentId = incident.IncidentId,
                TripId = trip.TripId,
                Action = "CUSTOMER_ORDER_AT_ROUTE_WAREHOUSE",
                Title = $"Đơn {order.TrackingCode} đã về kho tuyến",
                Body = "Hàng đã được đưa về kho an toàn. ColdChainX đang ưu tiên sắp xếp xe khác để tiếp tục giao đơn.",
                AdditionalUserIds = new[] { customerUserId.Value },
                IncludeReporter = false,
                IncludeTripDrivers = false,
                RealtimeEventName = "CustomerOrderAwaitingReplacementVehicle",
                NotificationType = "ORDER_DELAYED",
                ReferenceId = order.OrderId.ToString(),
                Screen = "ORDER_DETAIL",
                AdditionalData = new Dictionary<string, string>
                {
                    ["orderId"] = order.OrderId.ToString(),
                    ["trackingCode"] = order.TrackingCode
                },
                Payload = new { incident.IncidentId, order.OrderId, order.TrackingCode, warehouse.WarehouseName }
            });
        }
    }

    private static string FormatLpnCodes(IReadOnlyCollection<string> lpnCodes)
    {
        var codes = lpnCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct().ToList();
        var visible = string.Join(", ", codes.Take(10));
        return codes.Count > 10 ? $"{visible} và {codes.Count - 10} LPN khác" : visible;
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

    private static bool IsValidEvidenceUrl(string url)
        => !string.IsNullOrWhiteSpace(url)
           && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}
