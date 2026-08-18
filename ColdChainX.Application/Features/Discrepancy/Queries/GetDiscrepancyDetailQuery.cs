using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Helpers;
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
                .ThenInclude(o => o.OrderDimension)
            .Include(l => l.Receipt)
                .ThenInclude(r => r.Warehouse)
            .Include(l => l.Receipt)
                .ThenInclude(r => r.Receiver)
            .Include(l => l.PackageVariantLines)
                .ThenInclude(line => line.OrderPackageVariant)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return null;

        var order = lpn.Order;
        var receipt = lpn.Receipt;
        var expectedQuantity = lpn.PackageVariantLines.Count > 0
            ? lpn.PackageVariantLines.Sum(line => line.Quantity)
            : order?.Quantity ?? 0;
        var expectedWeight = lpn.PackageVariantLines.Count > 0
            ? lpn.PackageVariantLines.Sum(line => line.ExpectedWeightKg)
            : order?.OrderDimension?.ExpectedWeightKg ?? 0m;
        var expectedCbm = lpn.PackageVariantLines.Count > 0
            ? lpn.PackageVariantLines.Sum(line => line.ExpectedCbm)
            : order?.OrderDimension == null
                ? 0m
                : InboundQcMeasurementCalculator.CalculateExpectedCbm(order.OrderDimension, order.Quantity);
        var singleLine = lpn.PackageVariantLines.Count == 1 ? lpn.PackageVariantLines.Single() : null;
        var compareDimensions = lpn.PackageVariantLines.Count <= 1;
        var expectedLength = singleLine?.OrderPackageVariant?.LengthCm ?? order?.OrderDimension?.LengthCm ?? 0m;
        var expectedWidth = singleLine?.OrderPackageVariant?.WidthCm ?? order?.OrderDimension?.WidthCm ?? 0m;
        var expectedHeight = singleLine?.OrderPackageVariant?.HeightCm ?? order?.OrderDimension?.HeightCm ?? 0m;
        var actualLength = singleLine?.LengthCm ?? lpn.LengthCm ?? 0m;
        var actualWidth = singleLine?.WidthCm ?? lpn.WidthCm ?? 0m;
        var actualHeight = singleLine?.HeightCm ?? lpn.HeightCm ?? 0m;

        return new DiscrepancyDetailsResponse
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            OrderId = lpn.OrderId,
            TrackingCode = order?.TrackingCode ?? "N/A",
            ItemName = order?.ItemName ?? "Unknown",
            ExpectedQuantity = expectedQuantity,
            ActualQuantity = lpn.Quantity,
            Quantity = lpn.Quantity,
            ExpectedWeightKg = expectedWeight,
            ActualWeightKg = lpn.ActualWeightKg,
            ExpectedCbm = expectedCbm,
            ActualCbm = lpn.ActualCbm,
            ExpectedLengthCm = compareDimensions ? expectedLength : 0m,
            ActualLengthCm = compareDimensions ? actualLength : 0m,
            ExpectedWidthCm = compareDimensions ? expectedWidth : 0m,
            ActualWidthCm = compareDimensions ? actualWidth : 0m,
            ExpectedHeightCm = compareDimensions ? expectedHeight : 0m,
            ActualHeightCm = compareDimensions ? actualHeight : 0m,
            IsQuantityDifferent = expectedQuantity != lpn.Quantity,
            IsWeightDifferent = Math.Abs(expectedWeight - lpn.ActualWeightKg) > 0.01m,
            IsCbmDifferent = Math.Abs(expectedCbm - lpn.ActualCbm) > 0.0001m,
            IsLengthDifferent = compareDimensions && Math.Abs(expectedLength - actualLength) > 0.01m,
            IsWidthDifferent = compareDimensions && Math.Abs(expectedWidth - actualWidth) > 0.01m,
            IsHeightDifferent = compareDimensions && Math.Abs(expectedHeight - actualHeight) > 0.01m,
            RequiredTemperature = lpn.RequiredTemperature,
            RecordedTemperature = lpn.RecordedTemperature,
            EvidenceImageUrl = lpn.EvidenceImageUrl,
            DiscrepancyReason = lpn.DiscrepancyReason,
            CreatedAt = lpn.CreatedAt,
            PackageLines = lpn.PackageVariantLines.Select(line => new LpnPackageVariantLineResponse
            {
                LpnPackageVariantLineId = line.LpnPackageVariantLineId,
                OrderPackageVariantId = line.OrderPackageVariantId,
                VariantName = line.VariantName,
                PackingType = line.PackingType,
                Quantity = line.Quantity,
                ExpectedWeightKg = line.ExpectedWeightKg,
                ActualWeightKg = line.ActualWeightKg,
                ExpectedCbm = line.ExpectedCbm,
                ActualCbm = line.ActualCbm,
                LengthCm = line.LengthCm,
                WidthCm = line.WidthCm,
                HeightCm = line.HeightCm,
                DiffPercent = line.DiffPercent,
                HasDiscrepancy = line.HasDiscrepancy
            }).ToList(),
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
