using System;
using Microsoft.AspNetCore.Http;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Attachment
{
    /// <summary>
    /// Request payload for uploading a compliance/evidence attachment.
    /// </summary>
    public class UploadAttachmentRequest
    {
        /// <summary>
        /// Raw binary file form-data content.
        /// </summary>
        public IFormFile File { get; set; } = null!;

        /// <summary>
        /// General category of the attachment (e.g. COMPLIANCE, INBOUND_EVIDENCE).
        /// </summary>
        public AttachmentCategory Category { get; set; }

        /// <summary>
        /// Detail sub-category (e.g. TEMPERATURE_LOG, PHYTOSANITARY_CERTIFICATE).
        /// </summary>
        public AttachmentSubCategory SubCategory { get; set; }

        /// <summary>
        /// Unique identifier of the associated Warehouse Receipt (optional polymorphic link).
        /// </summary>
        public Guid? WarehouseReceiptId { get; set; }

        /// <summary>
        /// Unique identifier of the associated Warehouse Receipt Item (optional polymorphic link).
        /// </summary>
        public Guid? WarehouseReceiptItemId { get; set; }

        /// <summary>
        /// Unique identifier of the associated Inventory Adjustment (optional polymorphic link).
        /// </summary>
        public Guid? InventoryAdjustmentId { get; set; }

        /// <summary>
        /// Unique identifier of the associated Outbound Order (optional polymorphic link).
        /// </summary>
        public Guid? OutboundOrderId { get; set; }

        /// <summary>
        /// Official document/permit registration number (optional).
        /// </summary>
        public string? DocumentNumber { get; set; }

        /// <summary>
        /// Issuing agency or body name (optional).
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Date when the document was issued (optional).
        /// </summary>
        public DateOnly? IssueDate { get; set; }

        /// <summary>
        /// Expiration date of the document/permit (optional).
        /// </summary>
        public DateOnly? ExpiryDate { get; set; }

        /// <summary>
        /// Numeric value captured from the document, e.g., temperature reading (optional).
        /// </summary>
        public decimal? CapturedValue { get; set; }

        /// <summary>
        /// Container seal ID or number written on the document (optional).
        /// </summary>
        public string? SealNumber { get; set; }
    }
}
