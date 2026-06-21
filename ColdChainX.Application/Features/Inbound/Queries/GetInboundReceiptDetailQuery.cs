using ColdChainX.Application.Features.Inbound.DTOs;
using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GetInboundReceiptDetailQuery : IRequest<InboundReceiptDetailDto?>
{
    public Guid Id { get; set; }

    public GetInboundReceiptDetailQuery(Guid id)
    {
        Id = id;
    }
}

public class GetInboundReceiptDetailQueryHandler : IRequestHandler<GetInboundReceiptDetailQuery, InboundReceiptDetailDto?>
{
    private readonly IApplicationDbContext _context;

    public GetInboundReceiptDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InboundReceiptDetailDto?> Handle(GetInboundReceiptDetailQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _context.WarehouseReceipts
            .Include(x => x.WarehouseReceiptItems)
            .Where(x => x.ReceiptId == request.Id)
            .Select(x => new InboundReceiptDetailDto
            {
                ReceiptId = x.ReceiptId,
                ReceiptCode = x.ReceiptCode,
                OrderId = x.OrderId,
                Status = x.ReceiptType,
                ArrivalTime = x.CreatedAt,
                CompletionTime = x.CreatedAt,
                DriverName = x.DelivererName,
                TruckPlate = "N/A",
                Items = x.WarehouseReceiptItems.Select(i => new InboundReceiptItemDto
                {
                    ReceiptItemId = i.ItemId,
                    ItemName = i.ItemName,
                    ExpectedQuantity = (int)i.ExpectedQty,
                    ActualQuantity = (int)i.ActualQty,
                    ConditionStatus = i.ConditionStatus ?? "GOOD"
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return receipt;
    }
}
