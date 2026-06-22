using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GetOutboundOrdersQuery : IRequest<List<OutboundOrderDto>>
{
}

public class GetOutboundOrdersQueryHandler : IRequestHandler<GetOutboundOrdersQuery, List<OutboundOrderDto>>
{
    private readonly IApplicationDbContext _context;

    public GetOutboundOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<OutboundOrderDto>> Handle(GetOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _context.TransportOrders
            .Include(x => x.Customer)
            .Where(x => x.Category == "OUTBOUND" || x.Category == "LTL")
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .Select(x => new OutboundOrderDto
            {
                OrderId = x.OrderId,
                OrderCode = x.TrackingCode,
                CustomerId = x.CustomerId,
                CustomerName = x.Customer != null ? x.Customer.CompanyName : "N/A",
                ServiceType = x.Category,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return orders;
    }
}
