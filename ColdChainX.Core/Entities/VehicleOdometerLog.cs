using System;

namespace ColdChainX.Core.Entities;

public partial class VehicleOdometerLog
{
    public Guid LogId { get; set; }

    public Guid VehicleId { get; set; }

    public double OdometerValue { get; set; }

    public string? LocationText { get; set; }

    public string? Reason { get; set; }

    public Guid? UpdatedBy { get; set; }

    public string? OdometerPhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Vehicle? Vehicle { get; set; }
}
