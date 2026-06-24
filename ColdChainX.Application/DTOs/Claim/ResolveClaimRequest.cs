namespace ColdChainX.Application.DTOs.Claim
{
    public class ResolveClaimRequest
    {
        public string Status { get; set; } = null!;
        public string? FaultOwner { get; set; }
        public string? ResolutionNote { get; set; }
    }
}
