using System;

namespace ColdChainX.Core.Entities;

public partial class ContractAppendix
{
    public Guid AppendixId { get; set; }

    public Guid? ContractId { get; set; }

    public Guid OrderId { get; set; }

    public string AppendixNumber { get; set; } = null!;

    public decimal AdjustedPrice { get; set; }

    public string? Reason { get; set; }

    public string Status { get; set; } = null!;

    public string? DraftHtmlContent { get; set; }

    public string? PdfUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual CustomerContract? Contract { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;
}
