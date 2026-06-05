using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class AlertLog
{
    public Guid AlertId { get; set; }

    public Guid? TripId { get; set; }

    public string AlertType { get; set; } = null!;

    public decimal? Value { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public string? Status { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? ResolutionNote { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ClaimEvidence> ClaimEvidences { get; set; } = new List<ClaimEvidence>();

    public virtual User? ResolvedByNavigation { get; set; }

    public virtual MasterTrip? Trip { get; set; }
}
