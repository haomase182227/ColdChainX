using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Features.Discrepancy.Queries;

public class GetDiscrepancyDetailQuery : IRequest<DiscrepancyDetailsResponse?>
{
    public Guid LpnId { get; set; }

    public GetDiscrepancyDetailQuery(Guid lpnId)
    {
        LpnId = lpnId;
    }
}

public class GetDiscrepancyDetailQueryHandler : IRequestHandler<GetDiscrepancyDetailQuery, DiscrepancyDetailsResponse?>
{
    private readonly IApplicationDbContext _context;

    public GetDiscrepancyDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DiscrepancyDetailsResponse?> Handle(GetDiscrepancyDetailQuery request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns
            .Include(l => l.Order)
            .Include(l => l.Receipt)
                .ThenInclude(r => r.Warehouse)
            .Include(l => l.Receipt)
                .ThenInclude(r => r.Receiver)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return null;

        var order = lpn.Order;
        var receipt = lpn.Receipt;

        return new DiscrepancyDetailsResponse
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            TrackingCode = order?.TrackingCode ?? "N/A",
            ItemName = order?.ItemName ?? "Unknown",
            Quantity = lpn.Quantity,
            ExpectedWeightKg = order?.ExpectedWeightKg ?? 0,
            ActualWeightKg = lpn.ActualWeightKg,
            ExpectedCbm = order?.ExpectedCbm ?? 0,
            ActualCbm = lpn.ActualCbm,
            RequiredTemperature = lpn.RequiredTemperature,
            RecordedTemperature = lpn.RecordedTemperature,
            EvidenceImageUrl = lpn.EvidenceImageUrl,
            DiscrepancyReason = lpn.DiscrepancyReason,
            CreatedAt = lpn.CreatedAt,
            ReceiptInfo = receipt == null ? null! : new DiscrepancyReceiptInfo
            {
                ReceiptId = receipt.ReceiptId,
                ReceiptCode = receipt.ReceiptCode,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = receipt.Warehouse?.WarehouseName ?? "N/A",
                RecordedTemperature = receipt.RecordedTemperature,
                DelivererName = receipt.DelivererName ?? "N/A",
                ReceiverName = receipt.Receiver?.FullName ?? "N/A",
                Note = receipt.Note,
                PdfUrl = receipt.PdfUrl,
                CreatedAt = receipt.CreatedAt
            }
        };
    }
}
