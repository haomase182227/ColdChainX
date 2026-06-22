using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GetOutboundPickListQuery : IRequest<List<OutboundPickListDto>>
{
    public Guid MasterTripId { get; set; }

    public GetOutboundPickListQuery(Guid masterTripId)
    {
        MasterTripId = masterTripId;
    }
}

public class GetOutboundPickListQueryHandler : IRequestHandler<GetOutboundPickListQuery, List<OutboundPickListDto>>
{
    private readonly IApplicationDbContext _context;

    public GetOutboundPickListQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<OutboundPickListDto>> Handle(GetOutboundPickListQuery request, CancellationToken cancellationToken)
    {
        var lpns = await _context.Lpns
            .Where(x => x.TripId == request.MasterTripId && x.State == LpnState.ALLOCATED)
            .Select(x => new OutboundPickListDto
            {
                LpnId = x.LpnId,
                LpnCode = x.LpnCode,
                ItemName = x.Order.ItemName,
                StorageLocation = x.StorageLocation ?? "N/A",
                Quantity = x.Quantity,
                Condition = x.DiscrepancyReason ?? "GOOD",
                Status = x.State.ToString()
            })
            .ToListAsync(cancellationToken);

        return lpns;
    }
}
