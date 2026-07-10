using ColdChainX.Application.Features.Outbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

using ColdChainX.Application.DTOs.Common;

namespace ColdChainX.Application.Features.Outbound.Queries;

public class GetOutboundOrdersQuery : IRequest<PagedResult<OutboundOrderDto>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetOutboundOrdersQueryHandler : IRequestHandler<GetOutboundOrdersQuery, PagedResult<OutboundOrderDto>>
{
    private readonly IApplicationDbContext _context;

    public GetOutboundOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OutboundOrderDto>> Handle(GetOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.TransportOrders
            .Include(x => x.Customer)
            .Where(x => x.Category == "OUTBOUND" || x.Category == "LTL")
            .OrderBy(x => x.CreatedAt);

        var totalRecords = await query.CountAsync(cancellationToken);

        var orders = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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

        return PagedResult<OutboundOrderDto>.Create(orders, totalRecords, request.PageNumber, request.PageSize);
    }
}
