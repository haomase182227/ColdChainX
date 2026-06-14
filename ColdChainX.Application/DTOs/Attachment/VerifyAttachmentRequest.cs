using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Attachment
{
    public class VerifyAttachmentRequest
    {
        public DocumentStatus Status { get; set; }
        public string? RejectionReason { get; set; }
    }
}
