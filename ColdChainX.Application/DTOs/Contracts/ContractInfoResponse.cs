namespace ColdChainX.Application.DTOs.Contracts
{
    public class ContractInfoResponse
    {
        public Guid ContractId { get; set; }
        public Guid OrderId { get; set; }
        public string ContractNumber { get; set; } = null!;
        public string? FileUrl { get; set; }
        public string? SignedFileUrl { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? UploadedSignedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string Status { get; set; } = null!;
    }
}
