namespace ColdChainX.Application.DTOs.Contracts
{
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
