using System;

namespace ColdChainX.Core.Entities;

/// <summary>
/// Junction between a <see cref="MasterTrip"/> and a <see cref="Driver"/>.
/// A trip can have 1–2 drivers (exactly 1 vehicle). For 2-driver trips the trip's
/// estimated duration is split evenly across the assigned drivers.
/// </summary>
public partial class TripDriver
{
    public Guid TripDriverId { get; set; }

    public Guid TripId { get; set; }

    public Guid DriverId { get; set; }

    /// <summary>PRIMARY (first driver) or SECONDARY (co-driver).</summary>
    public string DriverRole { get; set; } = "PRIMARY";

    /// <summary>Driving hours assigned to this driver for this trip (EstimatedDurationHours / driverCount).</summary>
    public decimal AssignedDurationHours { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual MasterTrip Trip { get; set; } = null!;

    public virtual Driver Driver { get; set; } = null!;
}
