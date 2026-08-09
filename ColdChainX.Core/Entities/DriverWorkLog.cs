using System;

namespace ColdChainX.Core.Entities;

public partial class DriverWorkLog
{
    public Guid WorkLogId { get; set; }

    public Guid DriverId { get; set; }

    public Guid? TripId { get; set; }

    public DateOnly WorkDate { get; set; }

    public decimal DrivingHours { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Driver Driver { get; set; } = null!;

    public virtual MasterTrip? Trip { get; set; }
}
