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
            .Include(x => x.Lpns)
                .ThenInclude(l => l.Order)
            .Include(x => x.Lpns)
                .ThenInclude(l => l.PackageVariantLines)
            .Where(x => x.ReceiptId == request.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (receipt == null)
            return null;

        var items = receipt.Lpns.SelectMany(lpn => lpn.PackageVariantLines.Count > 0
            ? lpn.PackageVariantLines.Select(line => new InboundReceiptItemDto
            {
                ReceiptItemId = line.LpnPackageVariantLineId,
                ItemName = $"{lpn.Order.ItemName} - {line.VariantName ?? "Default size"}",
                ExpectedQuantity = line.Quantity,
                ActualQuantity = line.Quantity,
                ConditionStatus = line.HasDiscrepancy ? "DISCREPANCY" : "GOOD"
            })
            : new[]
            {
                new InboundReceiptItemDto
                {
                    ReceiptItemId = lpn.LpnId,
                    ItemName = lpn.Order.ItemName,
                    ExpectedQuantity = lpn.Quantity,
                    ActualQuantity = lpn.Quantity,
                    ConditionStatus = lpn.State == ColdChainX.Core.Enums.LpnState.DISCREPANCY_HOLD ? "DISCREPANCY" : "GOOD"
                }
            }).ToList();

        return new InboundReceiptDetailDto
        {
            ReceiptId = receipt.ReceiptId,
            ReceiptCode = receipt.ReceiptCode,
            OrderId = receipt.OrderId,
            Status = receipt.ReceiptType,
            ArrivalTime = receipt.CreatedAt,
            CompletionTime = receipt.CreatedAt,
            DriverName = receipt.DelivererName,
            TruckPlate = "N/A",
            Items = items
        };
    }
}
