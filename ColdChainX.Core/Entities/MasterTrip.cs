using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class MasterTrip
{
    public Guid TripId { get; set; }

    public Guid? VehicleId { get; set; }

    public Guid? DriverId { get; set; }

    public Guid OriginLocationId { get; set; }

    public Guid DestinationLocationId { get; set; }

    public decimal? TotalDistanceKm { get; set; }

    public decimal TargetTemperature { get; set; }

    public DateTime PlannedStartTime { get; set; }

    public DateTime PlannedEndTime { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<AlertLog> AlertLogs { get; set; } = new List<AlertLog>();

    public virtual Location DestinationLocation { get; set; } = null!;

    public virtual Driver? Driver { get; set; }

    public virtual ICollection<ExpenseAdvance> ExpenseAdvances { get; set; } = new List<ExpenseAdvance>();

    public virtual ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();

    public virtual Location OriginLocation { get; set; } = null!;

    public virtual ICollection<Seal> Seals { get; set; } = new List<Seal>();

    public virtual ICollection<TelemetryLog> TelemetryLogs { get; set; } = new List<TelemetryLog>();

    public virtual ICollection<TransportOrder> TransportOrders { get; set; } = new List<TransportOrder>();

    public virtual ICollection<TripStop> TripStops { get; set; } = new List<TripStop>();

    public virtual Vehicle? Vehicle { get; set; }
}
