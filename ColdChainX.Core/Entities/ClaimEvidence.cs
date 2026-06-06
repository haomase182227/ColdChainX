using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class ClaimEvidence
{
    public Guid EvidenceId { get; set; }

    public Guid? ClaimId { get; set; }

    public string EvidenceType { get; set; } = null!;

    public Guid? AlertId { get; set; }

    public Guid? DocId { get; set; }

    public string? ImageUrl { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual AlertLog? Alert { get; set; }

    public virtual Claim? Claim { get; set; }

    public virtual TransportDocument? Doc { get; set; }

    public virtual User UploadedByNavigation { get; set; } = null!;
}
