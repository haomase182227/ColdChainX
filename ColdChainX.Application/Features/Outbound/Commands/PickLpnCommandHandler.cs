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
            return new PickLpnResponse { Success = false, Message = "LPN không tìm thấy." };

        if (lpn.State != LpnState.ALLOCATED)
            return new PickLpnResponse { Success = false, Message = $"LPN phải ở trạng thái ALLOCATED trước khi chất hàng. Trạng thái hiện tại: {lpn.State}" };

        if (lpn.TripId == null)
            return new PickLpnResponse { Success = false, Message = "LPN chưa được ghép vào chuyến nào." };

        var trip = await _context.MasterTrips.FirstOrDefaultAsync(t => t.TripId == lpn.TripId.Value, cancellationToken);
        if (trip == null)
            return new PickLpnResponse { Success = false, Message = "Không tìm thấy chuyến hàng của LPN này." };

        if (trip.Status != "PICKING")
            return new PickLpnResponse { Success = false, Message = $"Chuyến hàng phải ở trạng thái PICKING mới có thể lấy hàng. Trạng thái hiện tại: {trip.Status}" };

        lpn.State = LpnState.LOADING;
        lpn.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new PickLpnResponse { Success = true, Message = "LPN picked successfully." };
    }
}
