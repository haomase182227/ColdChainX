using MediatR;

namespace ColdChainX.Application.Features.Discrepancy.Commands;

public class ResolveDiscrepancyCommand : IRequest<ResolveDiscrepancyResponse>
{
    public Guid LpnId { get; set; }
    public bool Accept { get; set; }
    public decimal PenaltyAmount { get; set; }
    public string PenaltyReason { get; set; } = string.Empty;
}

public class ResolveDiscrepancyResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? PenaltyBillId { get; set; }
}
