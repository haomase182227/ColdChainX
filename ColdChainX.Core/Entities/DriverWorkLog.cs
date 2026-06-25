using System;

namespace ColdChainX.Core.Entities;

/// <summary>
/// Per-day driving-hour record for a driver. Used to enforce the legal limits of
/// 10 driving hours/day and 48 driving hours/week (calendar day UTC + calendar week Mon–Sun).
/// </summary>
public partial class DriverWorkLog
{
    public Guid WorkLogId { get; set; }

    public Guid DriverId { get; set; }

    /// <summary>The trip these hours were logged against (null if cancelled/manual).</summary>
    public Guid? TripId { get; set; }

    /// <summary>Calendar day the hours count toward.</summary>
    public DateOnly WorkDate { get; set; }

    /// <summary>Driving hours logged for this driver on <see cref="WorkDate"/> for this trip.</summary>
    public decimal DrivingHours { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Driver Driver { get; set; } = null!;

    public virtual MasterTrip? Trip { get; set; }
}
