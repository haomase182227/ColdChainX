using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Seal
{
    public Guid SealId { get; set; }

    public Guid? TripId { get; set; }

    public Guid? StopId { get; set; }

    public string SealCode { get; set; } = null!;

    public DateTime? AppliedAt { get; set; }

    public string? AppliedImageUrl { get; set; }

    public DateTime? RemovedAt { get; set; }

    public string? RemovedImageUrl { get; set; }

    public string? Status { get; set; }

    public string? Note { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TripStop? Stop { get; set; }

    public virtual MasterTrip? Trip { get; set; }
}
