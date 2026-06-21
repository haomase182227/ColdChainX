using ColdChainX.Application.Features.Inbound.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GetInboundReceiptsQuery : IRequest<List<InboundReceiptDto>>
{
}

public class GetInboundReceiptsQueryHandler : IRequestHandler<GetInboundReceiptsQuery, List<InboundReceiptDto>>
{
    private readonly IApplicationDbContext _context;

    public GetInboundReceiptsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<InboundReceiptDto>> Handle(GetInboundReceiptsQuery request, CancellationToken cancellationToken)
    {
        var receipts = await _context.WarehouseReceipts
            .Include(x => x.Order)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
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

        return receipts;
    }
}
