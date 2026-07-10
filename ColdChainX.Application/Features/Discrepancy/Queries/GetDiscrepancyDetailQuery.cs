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
            ExpectedQuantity = order?.Quantity ?? 0,
            ActualQuantity = lpn.Quantity,
            Quantity = lpn.Quantity,
            ExpectedWeightKg = order?.OrderDimension?.ExpectedWeightKg ?? 0,
            ActualWeightKg = lpn.ActualWeightKg,
            ExpectedCbm = order?.OrderDimension?.ExpectedCbm ?? 0,
            ActualCbm = lpn.ActualCbm,
            ExpectedLengthCm = order?.OrderDimension?.LengthCm ?? 0,
            ActualLengthCm = lpn.LengthCm ?? 0,
            ExpectedWidthCm = order?.OrderDimension?.WidthCm ?? 0,
            ActualWidthCm = lpn.WidthCm ?? 0,
            ExpectedHeightCm = order?.OrderDimension?.HeightCm ?? 0,
            ActualHeightCm = lpn.HeightCm ?? 0,
            IsQuantityDifferent = order != null && order.Quantity != lpn.Quantity,
            IsWeightDifferent = order != null && Math.Abs((order.OrderDimension?.ExpectedWeightKg ?? 0m) - lpn.ActualWeightKg) > 0.01m,
            IsCbmDifferent = order != null && Math.Abs((order.OrderDimension?.ExpectedCbm ?? 0m) - lpn.ActualCbm) > 0.0001m,
            IsLengthDifferent = order != null && Math.Abs((order.OrderDimension?.LengthCm ?? 0m) - (lpn.LengthCm ?? 0)) > 0.01m,
            IsWidthDifferent = order != null && Math.Abs((order.OrderDimension?.WidthCm ?? 0m) - (lpn.WidthCm ?? 0)) > 0.01m,
            IsHeightDifferent = order != null && Math.Abs((order.OrderDimension?.HeightCm ?? 0m) - (lpn.HeightCm ?? 0)) > 0.01m,
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
