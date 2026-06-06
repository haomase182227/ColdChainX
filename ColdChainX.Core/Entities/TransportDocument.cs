using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class TransportDocument
{
    public Guid DocId { get; set; }

    public Guid? OrderId { get; set; }

    public string DocType { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;

    public string? Status { get; set; }

    public Guid? VerifiedBy { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? RejectReason { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ClaimEvidence> ClaimEvidences { get; set; } = new List<ClaimEvidence>();

    public virtual TransportOrder? Order { get; set; }

    public virtual User UploadedByNavigation { get; set; } = null!;

    public virtual User? VerifiedByNavigation { get; set; }
}
