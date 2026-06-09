namespace ColdChainX.Application.DTOs.Contracts
{
    public class GenerateContractResponse
    {
        public Guid ContractId { get; set; }
        public Guid OrderId { get; set; }
        public string ContractNumber { get; set; } = null!;
        public string FileUrl { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
