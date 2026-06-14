using System;
using Microsoft.AspNetCore.Http;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Attachment
{
    public class UploadAttachmentRequest
    {
        public IFormFile File { get; set; } = null!;
        public AttachmentCategory Category { get; set; }
        public AttachmentSubCategory SubCategory { get; set; }

        // Relationship Target Bindings (Polymorphic targets)
        public Guid? WarehouseReceiptId { get; set; }
        public Guid? WarehouseReceiptItemId { get; set; }
        public Guid? InventoryAdjustmentId { get; set; }
        public Guid? OutboundOrderId { get; set; }

        // Optional metadata
        public string? DocumentNumber { get; set; }
        public string? Issuer { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public decimal? CapturedValue { get; set; }
        public string? SealNumber { get; set; }
    }
}
