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
    private readonly IPdfService _pdfService;

    public CompleteTripLoadingCommandHandler(
        IApplicationDbContext context,
        ILogger<CompleteTripLoadingCommandHandler> logger,
        IMediator mediator,
        IPdfService pdfService)
    {
        _context = context;
        _logger = logger;
        _mediator = mediator;
        _pdfService = pdfService;
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

        string? manifestUrl = null;
        string? outboundTicketUrl = null;

        try
        {
            manifestUrl = await _pdfService.GenerateManifestPdfAsync(trip.TripId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Manifest PDF cho trip {TripId}.", trip.TripId);
        }

        try
        {
            outboundTicketUrl = await _pdfService.GenerateOutboundTicketPdfAsync(trip.TripId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể sinh Phiếu Xuất Kho PDF cho trip {TripId}.", trip.TripId);
        }

        return new CompleteTripLoadingResponse
        {
            Success = true,
            Message = $"Trip {trip.TripId} loaded successfully with {lpns.Count} LPNs.",
            ManifestPdfUrl = manifestUrl,
            HandoverPdfUrl = null,
            OutboundTicketPdfUrl = outboundTicketUrl
        };
    }
}
