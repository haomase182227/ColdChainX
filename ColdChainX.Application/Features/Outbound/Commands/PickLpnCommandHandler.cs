using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Commands;

public class PickLpnCommandHandler : IRequestHandler<PickLpnCommand, PickLpnResponse>
{
    private readonly IApplicationDbContext _context;

    public PickLpnCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PickLpnResponse> Handle(PickLpnCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
        {
            return new PickLpnResponse { Success = false, Message = "LPN not found." };
        }

        if (lpn.State != LpnState.IN_STOCK && lpn.State != LpnState.ALLOCATED)
        {
            return new PickLpnResponse { Success = false, Message = $"Cannot pick LPN from state: {lpn.State}" };
        }

        lpn.State = LpnState.PICKED;
        lpn.PickedAt = DateTime.UtcNow;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new PickLpnResponse 
        { 
            Success = true, 
            Message = "LPN picked successfully."
        };
    }
}
