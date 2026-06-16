namespace ColdChainX.Application.DTOs.Contracts
{
    /// <summary>
    /// Response sau khi customer upload bản hợp đồng đã ký.
    /// Không bao gồm fileUrl (PDF nháp) và draftHtmlContent.
    /// </summary>
    public class UploadSignedContractResponse
    {
        public Guid ContractId { get; set; }
        public Guid OrderId { get; set; }
        public string ContractNumber { get; set; } = null!;
        public string? SignedFileUrl { get; set; }
        public DateTime? UploadedSignedAt { get; set; }
        public string Status { get; set; } = null!;
    }
}
