using System;

namespace ColdChainX.Core.Entities;

public partial class TripDriver
{
    public Guid TripDriverId { get; set; }

    public Guid TripId { get; set; }

    public Guid DriverId { get; set; }

    public string DriverRole { get; set; } = "PRIMARY";

    public decimal AssignedDurationHours { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual MasterTrip Trip { get; set; } = null!;

    public virtual Driver Driver { get; set; } = null!;
}
