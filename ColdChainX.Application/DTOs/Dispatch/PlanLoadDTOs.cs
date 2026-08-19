using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Dispatch;


public class PlanLoadRequest
{
    public List<Guid> LpnIds { get; set; } = new();

    public Guid VehicleId { get; set; }

    public Guid OriginWarehouseLocationId { get; set; }

    public DateTime PlannedStartTime { get; set; }

    public DateTime PlannedEndTime { get; set; }
    public string? ScreenshotBase64 { get; set; }

    public Guid? DispatchCoordinatorId { get; set; }
}


public class PlanLoadResult
{
    public Guid TripId { get; set; }

    public VehicleInfo Vehicle { get; set; } = null!;

    public RouteDetailsDto RouteDetails { get; set; } = null!;

    public List<LoadInstruction> LoadPlan { get; set; } = new();

    public string? ScreenshotBase64 { get; set; }

    public List<DispatchInstruction> DispatchInstructions { get; set; } = new();

    public int NotifiedCoordinators { get; set; }
}

public class VehicleInfo
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public decimal MaxWeightKg { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal TotalOrderWeightKg { get; set; }
    public decimal TotalOrderCbm { get; set; }
    public decimal WeightUtilizationPct { get; set; }
    public decimal CbmUtilizationPct { get; set; }
}

public class RouteDetailsDto
{
    public double TotalDistanceKm { get; set; }
    public int TotalDurationMinutes { get; set; }
    public string OverviewPolyline { get; set; } = null!;
    public decimal OriginLat { get; set; }
    public decimal OriginLng { get; set; }
    public string OriginAddress { get; set; } = null!;
    public decimal DestinationLat { get; set; }
    public decimal DestinationLng { get; set; }
    public string DestinationAddress { get; set; } = null!;
    public List<StopDto> Stops { get; set; } = new();
    public List<StepDto> Steps { get; set; } = new();
}

public class StepDto
{
    public string Instruction { get; set; } = null!;
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? Maneuver { get; set; }
}

public class StopDto
{
    public int Sequence { get; set; }
    public Guid LocationId { get; set; }
    public string Address { get; set; } = null!;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    public decimal DistanceFromPreviousKm { get; set; }

    public List<LpnSummary> LpnsToUnload { get; set; } = new();
}

public class OrderSummary
{
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;
}

public class LpnSummary
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string OrderTrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;
}

public class LoadInstruction
{
    public int LoadOrder { get; set; }

    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = null!;

    public string Zone { get; set; } = null!;

    public Guid DeliveryLocationId { get; set; }

    public int DeliveryStopSequence { get; set; }

    public string Reason { get; set; } = null!;
}

public class DispatchInstruction
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string Action { get; set; } = "LOAD"; // LOAD | STAGE
    public string PreviousStatus { get; set; } = null!;
    public string TargetStatus { get; set; } = "LOADING";
    public int LoadOrder { get; set; }
    public string Zone { get; set; } = null!;
}


public class ManualDispatchRequest
{
    public Guid? IncidentId { get; set; }

    public Guid? DispatcherId { get; set; }

    public Guid? ScheduleId { get; set; }

    public List<Guid> LpnIds { get; set; } = new();

    public Guid VehicleId { get; set; }

    public List<Guid> DriverIds { get; set; } = new();

    public Guid OriginWarehouseLocationId { get; set; }

    public DateTime PlannedStartTime { get; set; }

    public DateTime PlannedEndTime { get; set; }

    public string? ScreenshotBase64 { get; set; }
}

public class ManualDispatchFormRequest
{
    public string? IncidentId { get; set; }

    public string? ScheduleId { get; set; }

    public string VehicleId { get; set; } = string.Empty;

    public List<string> DriverIds { get; set; } = new();

    public DateTime PlannedStartTime { get; set; }
    
    public DateTime PlannedEndTime { get; set; }
    
    public string? ScreenshotBase64 { get; set; }
}

public class ManualDispatchResult
{
    public Guid TripId { get; set; }
    public VehicleInfo Vehicle { get; set; } = null!;

    public List<DriverInfo> Drivers { get; set; } = new();

    public decimal EstimatedDurationHours { get; set; }

    public List<LpnSummary> SelectedLpns { get; set; } = new();

    public RouteDetailsDto RouteDetails { get; set; } = null!;

    public List<LoadInstruction> LoadPlan { get; set; } = new();

    public string? ScreenshotBase64 { get; set; }

    public List<DispatchInstruction> DispatchInstructions { get; set; } = new();
    
    public int NotifiedCoordinators { get; set; }

    public string? LifoPdfUrl { get; set; }

    public string? SlaWarning { get; set; }

    public int LateLpnCount { get; set; }

    public int? SuggestedMaxPayloadKg { get; set; }
}

public record StartPickingResult(Guid TripId, string Status, int LpnCount);

public class CancelTripResult
{
    public Guid TripId { get; set; }
    public string PreviousStatus { get; set; } = null!;
    public string NewStatus { get; set; } = "CANCELLED";

    public int ResetLpnCount { get; set; }

    public int ResetOrderCount { get; set; }

    public int CancelledSealCount { get; set; }

    public int VoidedDocumentCount { get; set; }

    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public DateTime CancelledAt { get; set; }
    public string Message { get; set; } = null!;
}

public class DriverInfo
{
    public Guid DriverId { get; set; }
    public string FullName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string? IdentityNumber { get; set; }
    public string? LicenseClass { get; set; }
    public DateOnly? LicenseExpiry { get; set; }
    public string LicenseStatus { get; set; } = null!; // VALID, EXPIRING_SOON, EXPIRED
    public string DriverRole { get; set; } = null!; // PRIMARY, SECONDARY
    public decimal AssignedDurationHours { get; set; }
}



public class WarehouseOrderResult
{
    public Guid TripId { get; set; }
    public string Status { get; set; } = null!; // PENDING_WH_APPROVAL, APPROVED, WH_REJECTED
    public string? RejectionReason { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public VehicleInfo? Vehicle { get; set; }

    public List<OrderSummary> Orders { get; set; } = new();

    public List<LoadInstruction>? LoadPlan { get; set; }

    public int NotifiedUsers { get; set; }
}

public class RejectWarehouseOrderRequest
{
    public string Reason { get; set; } = null!;
}


public class VehicleIoTStatus
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public bool HasIoTDevices { get; set; }
    public string OverallStatus { get; set; } = null!; // ONLINE, OFFLINE, PARTIAL, NO_DEVICE

    public List<IoTDeviceStatus> Devices { get; set; } = new();
}

public class IoTDeviceStatus
{
    public Guid DeviceId { get; set; }
    public int? BatteryLevel { get; set; }
    public DateTime? LastPingTime { get; set; }
    public string? Status { get; set; }
    public bool IsOnline { get; set; } // LastPingTime < 10 phút trước

    public LatestTelemetry? LatestTelemetry { get; set; }
}

public class LatestTelemetry
{
    public decimal Temperature { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}


public class SealAndDispatchRequest
{
    public string SealCode { get; set; } = null!;
}

public class SealAndDispatchResult
{
    public Guid TripId { get; set; }
    public string SealCode { get; set; } = null!;
    public bool AllOrdersLoaded { get; set; }
    public int TotalOrders { get; set; }
    public int LoadedOrders { get; set; }
    public DateTime SealedAt { get; set; }
    public Guid SealedBy { get; set; }
    public string TripStatus { get; set; } = null!;

    public string? WaybillUrl { get; set; }
}


public class ProcessBacklogRequest
{
    public string OriginWarehouseLocationId { get; set; } = null!;

    public int BacklogDays { get; set; } = 1;

    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
    public string? ScreenshotBase64 { get; set; }
}

public class BacklogDispatchResult
{
    public List<BacklogTripSummary> DispatchedTrips { get; set; } = new();

    public List<OrderSummary> SkippedOrders { get; set; } = new();

    public int TotalProcessed { get; set; }
    public int TotalSkipped { get; set; }
}

public class BacklogTripSummary
{
    public Guid TripId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public string DriverName { get; set; } = null!;
    public int OrderCount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string TempCondition { get; set; } = null!;
}


public class GoongDirectionsResult
{
    public decimal TotalDistanceKm { get; set; }
    public int TotalDurationSeconds { get; set; }
    public string? OverviewPolyline { get; set; }
    public List<GoongLeg> Legs { get; set; } = new();
}

public sealed class TripRouteResponse
{
    public Guid TripId { get; set; }

    public string? OverviewPolyline { get; set; }

    public int TotalDistanceMeters { get; set; }

    public int TotalDurationSeconds { get; set; }

    public TripRoutePointDto Origin { get; set; } = null!;

    public TripRoutePointDto Destination { get; set; } = null!;

    public IReadOnlyList<int> WaypointOrder { get; set; } = Array.Empty<int>();

    public List<OptimizedTripStopDto> OptimizedStops { get; set; } = new();
}

public sealed class TripRoutePointDto
{
    public Guid LocationId { get; set; }

    public string Address { get; set; } = string.Empty;

    public decimal Lat { get; set; }

    public decimal Lon { get; set; }
}

public sealed class OptimizedTripStopDto
{
    public Guid StopId { get; set; }

    public Guid LocationId { get; set; }

    public int OriginalStopSequence { get; set; }

    public int OptimizedSequence { get; set; }

    public string StopType { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public decimal Lat { get; set; }

    public decimal Lon { get; set; }

    public List<TripRouteOrderDto> Orders { get; set; } = new();

    public List<LpnSummary> Lpns { get; set; } = new();
}

public sealed class TripRouteOrderDto
{
    public Guid OrderId { get; set; }

    public string TrackingCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal WeightKg { get; set; }

    public decimal Cbm { get; set; }

    public string TempCondition { get; set; } = string.Empty;
}

public sealed class GoongOptimizedRouteResult
{
    public string? OverviewPolyline { get; set; }

    public int TotalDistanceMeters { get; set; }

    public int TotalDurationSeconds { get; set; }

    public IReadOnlyList<int> WaypointOrder { get; set; } = Array.Empty<int>();
}

public class GoongLeg
{
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? StartAddress { get; set; }
    public string? EndAddress { get; set; }
    public List<GoongStep> Steps { get; set; } = new();
}

public class GoongStep
{
    public string Instruction { get; set; } = null!;
    public decimal DistanceKm { get; set; }
    public int DurationSeconds { get; set; }
    public string? Maneuver { get; set; }
}
