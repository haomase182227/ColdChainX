using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class IotDevice
{
    public Guid DeviceId { get; set; }

    public string? DeviceCode { get; set; }

    public Guid? VehicleId { get; set; }

    public int? BatteryLevel { get; set; }

    public DateTime? LastPingTime { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<TelemetryLog> TelemetryLogs { get; set; } = new List<TelemetryLog>();

    public virtual Vehicle? Vehicle { get; set; }

    public bool IsOnline { get; set; }
}
