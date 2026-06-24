using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

/// <summary>
/// Lay danh sach LPN dang o trang thai LOADING — san sang de goi POST /api/Outbound/pick.
/// Co the loc theo TripId (tuy chon).
/// </summary>
public class GetAvailableLpnsQuery : IRequest<List<AvailableLpnDto>>
{
    public Guid? TripId { get; set; }

    public GetAvailableLpnsQuery(Guid? tripId = null)
    {
        TripId = tripId;
    }
}

public class GetAvailableLpnsQueryHandler : IRequestHandler<GetAvailableLpnsQuery, List<AvailableLpnDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAvailableLpnsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<AvailableLpnDto>> Handle(GetAvailableLpnsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Lpns
            .Where(x => x.State == LpnState.LOADING);

        if (request.TripId.HasValue)
            query = query.Where(x => x.TripId == request.TripId.Value);

        var lpns = await query
            .OrderBy(x => x.LpnCode)
            .Select(x => new AvailableLpnDto
            {
                LpnId = x.LpnId,
                LpnCode = x.LpnCode,
                TripId = x.TripId,
                OrderId = x.OrderId,
                OrderCode = x.Order.TrackingCode,
                ItemName = x.Order.ItemName,
                StorageLocation = x.StorageLocation ?? "N/A",
                Quantity = x.Quantity,
                State = x.State.ToString()
            })
            .ToListAsync(cancellationToken);

        return lpns;
    }
}
