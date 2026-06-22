using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class PutawayLpnCommandHandler : IRequestHandler<PutawayLpnCommand, PutawayLpnResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IMediator _mediator;

    public PutawayLpnCommandHandler(IApplicationDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<PutawayLpnResponse> Handle(PutawayLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
        {
            return new PutawayLpnResponse { Success = false, Message = "LPN not found." };
        }

        if (lpn.State != LpnState.RECEIVING)
        {
            return new PutawayLpnResponse { Success = false, Message = $"LPN is not in RECEIVING state. Current state: {lpn.State}" };
        }

        if (string.IsNullOrWhiteSpace(request.StorageLocation))
        {
            return new PutawayLpnResponse { Success = false, Message = "StorageLocation is required." };
        }

        lpn.StorageLocation = request.StorageLocation;
        lpn.InboundTime = DateTime.UtcNow;
        lpn.State = LpnState.IN_STOCK;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Publish Event
        await _mediator.Publish(new Events.LpnPutawayCompletedEvent(lpn.OrderId, lpn.LpnId), cancellationToken);

        return new PutawayLpnResponse 
        { 
            Success = true, 
            Message = $"LPN successfully putaway to {request.StorageLocation}."
        };
    }
}
