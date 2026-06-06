using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Claim
{
    public Guid ClaimId { get; set; }

    public string ClaimCode { get; set; } = null!;

    public Guid? OrderId { get; set; }

    public string ClaimType { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? FaultOwner { get; set; }

    public string? Status { get; set; }

    public string? ResolutionNote { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual ICollection<ClaimEvidence> ClaimEvidences { get; set; } = new List<ClaimEvidence>();

    public virtual TransportOrder? Order { get; set; }
}
