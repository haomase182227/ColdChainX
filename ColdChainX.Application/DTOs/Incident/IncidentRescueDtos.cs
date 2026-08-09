using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Incident
{
    public class DispatchRescueRequest
    {
        public Guid ReplacementVehicleId { get; set; }

        public int? TransloadMinutes { get; set; }

        public string? Note { get; set; }
    }

    public class RescueCandidateResponse
    {
        public Guid VehicleId { get; set; }
        public string TruckPlate { get; set; } = null!;
        public string VehicleType { get; set; } = null!;
        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public int IotDeviceCount { get; set; }
        public int OnlineIotDeviceCount { get; set; }
        public bool HasOnlineIot { get; set; }
        public string Label { get; set; } = null!;
    }

    public class ContinueTripAfterIncidentRequest
    {
        public string? HandlingNote { get; set; }
    }

    public class ConfirmTransloadRequest
    {
        public string ConfirmationNote { get; set; } = null!;
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
