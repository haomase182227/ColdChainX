using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities;

public class AttachmentAuditHistory
{
    public Guid HistoryId { get; set; }
    public Guid AttachmentId { get; set; }
    
    public DocumentStatus? PreviousStatus { get; set; }
    public DocumentStatus NewStatus { get; set; }
    
    public string? Reason { get; set; }
    
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual WarehouseEvidenceAttachment Attachment { get; set; } = null!;
}
