using ColdChainX.Application.Features.Inbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

using ColdChainX.Application.DTOs.Common;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GetInboundReceiptsQuery : IRequest<PagedResult<InboundReceiptDto>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetInboundReceiptsQueryHandler : IRequestHandler<GetInboundReceiptsQuery, PagedResult<InboundReceiptDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInboundReceiptsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<InboundReceiptDto>> Handle(GetInboundReceiptsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.WarehouseReceipts
            .Include(x => x.Order)
            .OrderByDescending(x => x.CreatedAt);

        var totalRecords = await query.CountAsync(cancellationToken);

        var receipts = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new InboundReceiptDto
            {
                ReceiptId = x.ReceiptId,
                ReceiptCode = x.ReceiptCode,
                OrderId = x.OrderId,
                Status = x.ReceiptType,
                ArrivalTime = x.CreatedAt,
                CompletionTime = x.CreatedAt,
                DriverName = x.DelivererName,
                TruckPlate = "N/A"
            })
            .ToListAsync(cancellationToken);

        return PagedResult<InboundReceiptDto>.Create(receipts, totalRecords, request.PageNumber, request.PageSize);
    }
}
