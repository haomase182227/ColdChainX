using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Incident
{
    public class DispatchRescueRequest
    {
        public Guid ReplacementVehicleId { get; set; }

        public IncidentRescuePlanType PlanType { get; set; } = IncidentRescuePlanType.DIRECT_RESCUE;

        public Guid? DestinationWarehouseId { get; set; }

        public int? TransloadMinutes { get; set; }

        public string? Note { get; set; }
    }

    public class RescueCandidateResponse
    {
        public Guid VehicleId { get; set; }
        public string TruckPlate { get; set; } = null!;
        public string VehicleType { get; set; } = null!;
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string? WarehouseAddress { get; set; }
        public decimal? DistanceKm { get; set; }
        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public int IotDeviceCount { get; set; }
        public int OnlineIotDeviceCount { get; set; }
        public bool HasOnlineIot { get; set; }
        public int? EstimatedArrivalMinutes { get; set; }
        public bool? CanArriveWithinSafeTime { get; set; }
        public int? RemainingSafeTimeMinutes { get; set; }
        public decimal RemainingWeightCapacity { get; set; }
        public decimal RemainingCbmCapacity { get; set; }
        public int TransferCount { get; set; } = 1;
        public bool Recommended { get; set; }
        public string RecommendationReason { get; set; } = null!;
        public string Label { get; set; } = null!;
    }

    public class ContinueTripAfterIncidentRequest
    {
        public string? HandlingNote { get; set; }
        public int ExpectedDelayMinutes { get; set; }
    }

    public class ConfirmTransloadRequest
    {
        public string ConfirmationNote { get; set; } = null!;
        public List<Guid> LpnIds { get; set; } = new();
        public string? SealNumber { get; set; }
        public decimal? TransferTemperature { get; set; }
        public DateTime? TransferredAt { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationDescription { get; set; }
        public List<string> EvidenceUrls { get; set; } = new();
    }

    public sealed class TransloadRecord
    {
        public List<Guid> LpnIds { get; set; } = new();
        public string? SealNumber { get; set; }
        public decimal? TransferTemperature { get; set; }
        public DateTime TransferredAt { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationDescription { get; set; }
        public List<string> EvidenceUrls { get; set; } = new();
        public Guid ConfirmedBy { get; set; }
    }

    public sealed class InternalColdStorageOption
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string? Address { get; set; }
        public decimal? DistanceKm { get; set; }
        public int? EstimatedArrivalMinutes { get; set; }
        public bool? CanArriveWithinSafeTime { get; set; }
        public decimal? MinTemperature { get; set; }
        public decimal? MaxTemperature { get; set; }
        public int AvailablePalletPositions { get; set; }
    }

    public sealed class IncidentRescuePlanResponse
    {
        public Guid IncidentId { get; set; }
        public Guid TripId { get; set; }
        public decimal TargetTemperature { get; set; }
        public int? RemainingSafeTimeMinutes { get; set; }
        public bool TemperatureThresholdBreached { get; set; }
        public bool DirectDeliveryLocked { get; set; }
        public string RecommendedAction { get; set; } = null!;
        public string RecommendationReason { get; set; } = null!;
        public List<RescueCandidateResponse> Vehicles { get; set; } = new();
        public List<InternalColdStorageOption> InternalColdStorages { get; set; } = new();
        public bool RequiresExternalStorageSearch { get; set; }
        public bool RequiresManualEscalation { get; set; }
    }

    public sealed class RecordRescueFallbackRequest
    {
        public IncidentRescuePlanType PlanType { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? ExternalStorageName { get; set; }
        public string? ExternalStorageAddress { get; set; }
        public decimal? StorageTemperature { get; set; }
        public string? ConfirmedByName { get; set; }
        public string? RedispatchPlan { get; set; }
        public string Note { get; set; } = null!;
    }

    public sealed class RescueFallbackResult
    {
        public Guid IncidentId { get; set; }
        public Guid TripId { get; set; }
        public string IncidentStatus { get; set; } = null!;
        public string TripStatus { get; set; } = null!;
        public string PlanType { get; set; } = null!;
        public string PlanDetails { get; set; } = null!;
        public bool IncidentRemainsOpen { get; set; }
    }

    public class IncidentWorkflowResult
    {
        public Guid IncidentId { get; set; }
        public string IncidentStatus { get; set; } = null!;
        public Guid TripId { get; set; }
        public string TripStatus { get; set; } = null!;
        public Guid VehicleId { get; set; }
        public string VehiclePlate { get; set; } = null!;
        public DateTime ConfirmedAt { get; set; }
        public string Message { get; set; } = null!;
    }

    public class StopEtaChange
    {
        public Guid StopId { get; set; }
        public int StopSequence { get; set; }
        public string? Address { get; set; }
        public DateTime OldEta { get; set; }
        public DateTime NewEta { get; set; }
        public int DelayMinutes { get; set; }
        public int NotifiedCustomers { get; set; }
    }

    public class IncidentRescueResult
    {
        public Guid IncidentId { get; set; }
        public string IncidentStatus { get; set; } = null!;
        public Guid TripId { get; set; }
        public string TripStatus { get; set; } = null!;

        public Guid BrokenVehicleId { get; set; }
        public string BrokenVehiclePlate { get; set; } = null!;
        public string BrokenVehicleStatus { get; set; } = null!;
        public Guid? MaintenanceTicketId { get; set; }

        public Guid RescueVehicleId { get; set; }
        public string RescueVehiclePlate { get; set; } = null!;
        public string RescueVehicleStatus { get; set; } = null!;

        public int TransloadLpnCount { get; set; }

        public string EtaMethod { get; set; } = null!;

        public List<StopEtaChange> UpdatedStops { get; set; } = new();

        public int NotifiedCustomerCount { get; set; }

        public string Message { get; set; } = null!;
    }
}
