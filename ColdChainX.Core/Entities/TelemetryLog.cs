using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class TelemetryLog
{
    public Guid LogId { get; set; }

    public Guid? DeviceId { get; set; }

    public Guid? TripId { get; set; }

    public decimal Temperature { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public DateTime Timestamp { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual IotDevice? Device { get; set; }

    public virtual MasterTrip? Trip { get; set; }
}
