using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities;

public class WarehouseEvidenceAttachment
{
    public Guid AttachmentId { get; set; }
    
    // Core Attachment properties
    public string FileName { get; set; } = null!;
    public string FilePath { get; set; } = null!;
    public string? FileUrl { get; set; }
    public long FileSize { get; set; }
    public string ContentType { get; set; } = null!;

    // Enums
    public AttachmentFormat Format { get; set; }
    public AttachmentCategory Category { get; set; }
    public AttachmentSubCategory SubCategory { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.PENDING;

    // Document Metadata
    public string? DocumentNumber { get; set; }
    public string? Issuer { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal? CapturedValue { get; set; }
    public string? SealNumber { get; set; }
    
    // Verification Metadata
    public string? RejectionReason { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    // Relation bindings (Concrete polymorphic FKs)
    public Guid? WarehouseReceiptId { get; set; }
    public Guid? WarehouseReceiptItemId { get; set; }
    public Guid? InventoryAdjustmentId { get; set; }
    public Guid? OutboundOrderId { get; set; }

    // Upload Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }

    // Navigations
    public virtual WarehouseReceipt? WarehouseReceipt { get; set; }
    public virtual WarehouseReceiptItem? WarehouseReceiptItem { get; set; }
    public virtual InventoryAdjustment? InventoryAdjustment { get; set; }
    public virtual OutboundOrder? OutboundOrder { get; set; }
}
