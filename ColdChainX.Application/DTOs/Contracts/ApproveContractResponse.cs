namespace ColdChainX.Application.DTOs.Contracts
{
    public class ApproveContractResponse
    {
        public Guid ContractId { get; set; }
        public Guid OrderId { get; set; }
        public string ContractNumber { get; set; } = null!;
        public string ContractStatus { get; set; } = null!;
        public string OrderStatus { get; set; } = null!;
        public string TrackingCode { get; set; } = null!;
        public DateOnly? SignedDate { get; set; }
    }
}
