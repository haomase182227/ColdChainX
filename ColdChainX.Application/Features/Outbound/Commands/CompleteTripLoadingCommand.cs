using MediatR;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class CompleteTripLoadingCommand : IRequest<CompleteTripLoadingResponse>
{
    public Guid TripId { get; set; }
    public List<Guid> LoadedLpnIds { get; set; } = new();
}

public class CompleteTripLoadingResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ManifestPdfUrl { get; set; }
    public string? HandoverPdfUrl { get; set; }
    public string? OutboundTicketPdfUrl { get; set; }
}
