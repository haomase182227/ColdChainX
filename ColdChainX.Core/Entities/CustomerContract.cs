using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class CustomerContract
{
    public Guid ContractId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? OrderId { get; set; }

    public string ContractNumber { get; set; } = null!;

    public DateOnly? SignedDate { get; set; }

    public DateOnly ExpiredDate { get; set; }

    public string FileUrl { get; set; } = null!;

    public string? DraftHtmlContent { get; set; }

    public string? SignedFileUrl { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? UploadedSignedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public Guid? VerifiedBy { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual TransportOrder? Order { get; set; }
}
