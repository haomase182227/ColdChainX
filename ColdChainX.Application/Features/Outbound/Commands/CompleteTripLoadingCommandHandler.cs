using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class CompleteTripLoadingCommandHandler : IRequestHandler<CompleteTripLoadingCommand, CompleteTripLoadingResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CompleteTripLoadingCommandHandler> _logger;
    private readonly IMediator _mediator;

    public CompleteTripLoadingCommandHandler(IApplicationDbContext context, ILogger<CompleteTripLoadingCommandHandler> logger, IMediator mediator)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task<CompleteTripLoadingResponse> Handle(CompleteTripLoadingCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips.FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
        {
            return new CompleteTripLoadingResponse { Success = false, Message = "Trip not found." };
        }

        var lpns = await _context.Lpns
            .Where(l => request.LoadedLpnIds.Contains(l.LpnId))
            .ToListAsync(cancellationToken);

        if (!lpns.Any())
        {
            return new CompleteTripLoadingResponse { Success = false, Message = "No LPNs found to load." };
        }

        foreach (var lpn in lpns)
        {
            if (lpn.State != LpnState.PICKED)
            {
                return new CompleteTripLoadingResponse { Success = false, Message = $"LPN {lpn.LpnCode} is not in PICKED state (Current: {lpn.State})." };
            }

            lpn.State = LpnState.SHIPPED;
            lpn.UpdatedAt = DateTime.UtcNow;
            lpn.TripId = trip.TripId;
        }

        trip.SealNumber = request.SealNumber;
        trip.Status = "LOADING_COMPLETED";

        await _context.SaveChangesAsync(cancellationToken);

        // Publish Events for all loaded LPNs
        foreach (var lpn in lpns)
        {
            await _mediator.Publish(new Events.LpnShippedEvent(lpn.OrderId, lpn.LpnId), cancellationToken);
        }

        _logger.LogInformation($"[PDF_MOCK] Generating Manifest, Handover, and Outbound Ticket PDFs for Trip {trip.TripId}...");

        return new CompleteTripLoadingResponse 
        { 
            Success = true, 
            Message = $"Trip {trip.TripId} sealed successfully with {lpns.Count} LPNs.",
            ManifestPdfUrl = $"https://coldchainx.mock/api/docs/manifest/{trip.TripId}.pdf",
            HandoverPdfUrl = $"https://coldchainx.mock/api/docs/handover/{trip.TripId}.pdf",
            OutboundTicketPdfUrl = $"https://coldchainx.mock/api/docs/outbound/{trip.TripId}.pdf"
        };
    }
}
