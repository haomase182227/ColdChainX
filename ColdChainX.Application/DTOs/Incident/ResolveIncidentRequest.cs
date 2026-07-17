namespace ColdChainX.Application.DTOs.Incident
{
    public class ResolveIncidentRequest
    {
        public string ResolutionNote { get; set; } = null!;
        public decimal ReimbursedAmount { get; set; }
    }
}
