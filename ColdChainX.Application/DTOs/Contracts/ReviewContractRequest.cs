namespace ColdChainX.Application.DTOs.Contracts
{
    public class ReviewContractRequest
    {
        public string Action { get; set; } = null!;
        public string? CustomerNote { get; set; }
    }
}
