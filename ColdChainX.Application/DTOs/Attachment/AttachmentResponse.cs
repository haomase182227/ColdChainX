using System;

namespace ColdChainX.Application.DTOs.Attachment
{
    public class AttachmentResponse
    {
        public Guid AttachmentId { get; set; }
        public string FileName { get; set; } = null!;
        public string FilePath { get; set; } = null!;
        public string? FileUrl { get; set; }
        public long FileSize { get; set; }
        public string ContentType { get; set; } = null!;
        public string Format { get; set; } = null!;
        public string Category { get; set; } = null!;
        public string SubCategory { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? DocumentNumber { get; set; }
        public string? Issuer { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal? CapturedValue { get; set; }
        public string? SealNumber { get; set; }
        public string? RejectionReason { get; set; }
        public Guid? VerifiedBy { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public Guid? WarehouseReceiptId { get; set; }
        public Guid? WarehouseReceiptItemId { get; set; }
        public Guid? InventoryAdjustmentId { get; set; }
        public Guid? OutboundOrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
    }
}
