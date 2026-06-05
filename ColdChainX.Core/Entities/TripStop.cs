using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class TripStop
{
    public Guid StopId { get; set; }

    public Guid? TripId { get; set; }

    public Guid? LocationId { get; set; }

    public int StopSequence { get; set; }

    public string StopType { get; set; } = null!;

    public DateTime PlannedArrivalTime { get; set; }

    public DateTime PlannedDepartureTime { get; set; }

    public DateTime? ActualArrivalTime { get; set; }

    public DateTime? ActualDepartureTime { get; set; }

    public string? Status { get; set; }

    public string? Note { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Location? Location { get; set; }

    public virtual ICollection<Seal> Seals { get; set; } = new List<Seal>();

    public virtual MasterTrip? Trip { get; set; }
}
