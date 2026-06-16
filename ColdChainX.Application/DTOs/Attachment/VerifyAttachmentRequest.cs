using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Attachment
{
    /// <summary>
    /// Request payload for verifying/auditing an uploaded attachment.
    /// </summary>
    public class VerifyAttachmentRequest
    {
        /// <summary>
        /// Audited document verification status (e.g. APPROVED, REJECTED).
        /// </summary>
        public DocumentStatus Status { get; set; }

        /// <summary>
        /// Explanation details if the document status is marked as REJECTED.
        /// </summary>
        public string? RejectionReason { get; set; }
    }
}
