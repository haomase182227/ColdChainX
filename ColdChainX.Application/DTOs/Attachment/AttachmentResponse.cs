using System;

namespace ColdChainX.Application.DTOs.Attachment
{
    /// <summary>
    /// Response model representing uploaded attachment metadata.
    /// </summary>
    public class AttachmentResponse
    {
        /// <summary>
        /// Unique system identifier of the attachment.
        /// </summary>
        public Guid AttachmentId { get; set; }

        /// <summary>
        /// Original name of the uploaded file.
        /// </summary>
        public string FileName { get; set; } = null!;

        /// <summary>
        /// File path in storage.
        /// </summary>
        public string FilePath { get; set; } = null!;

        /// <summary>
        /// Download URL path to access the file.
        /// </summary>
        public string? FileUrl { get; set; }

        /// <summary>
        /// Size of the file in bytes.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// MIME Content type (e.g. image/png, application/pdf).
        /// </summary>
        public string ContentType { get; set; } = null!;

        /// <summary>
        /// Format file extension.
        /// </summary>
        public string Format { get; set; } = null!;

        /// <summary>
        /// Category (e.g., Compliance, InboundEvidence).
        /// </summary>
        public string Category { get; set; } = null!;

        /// <summary>
        /// Sub-category detailing document type (e.g. TemperatureLog, Phytosanitary).
        /// </summary>
        public string SubCategory { get; set; } = null!;

        /// <summary>
        /// Verification status of the document (e.g. PENDING, APPROVED, REJECTED).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Optional official document/permit reference number.
        /// </summary>
        public string? DocumentNumber { get; set; }

        /// <summary>
        /// Optional issuer authority of the document.
        /// </summary>
        public string? Issuer { get; set; }

        /// <summary>
        /// Optional official issue date.
        /// </summary>
        public DateOnly? IssueDate { get; set; }

        /// <summary>
        /// Optional official expiration date.
        /// </summary>
        public DateOnly? ExpiryDate { get; set; }

        /// <summary>
        /// Optional numeric value captured from the document (e.g. verified temperature).
        /// </summary>
        public decimal? CapturedValue { get; set; }

        /// <summary>
        /// Optional container seal number verified on the document.
        /// </summary>
        public string? SealNumber { get; set; }

        /// <summary>
        /// Reason text if the supervisor rejected the document.
        /// </summary>
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Unique identifier of the supervisor who verified the document.
        /// </summary>
        public Guid? VerifiedBy { get; set; }

        /// <summary>
        /// Timestamp when the verification occurred.
        /// </summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// Linked warehouse receipt identifier (if applicable).
        /// </summary>
        public Guid? WarehouseReceiptId { get; set; }

        /// <summary>
        /// Linked warehouse receipt item identifier (if applicable).
        /// </summary>
        public Guid? WarehouseReceiptItemId { get; set; }

        /// <summary>
        /// Linked inventory adjustment identifier (if applicable).
        /// </summary>
        public Guid? InventoryAdjustmentId { get; set; }

        /// <summary>
        /// Linked outbound order identifier (if applicable).
        /// </summary>
        public Guid? OutboundOrderId { get; set; }

        /// <summary>
        /// Timestamp when the attachment record was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who uploaded the attachment.
        /// </summary>
        public Guid CreatedBy { get; set; }
    }
}
