using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Discrepancy.Queries;

public class GenerateDiscrepancyPdfQuery : IRequest<byte[]>
{
    public Guid ReceiptId { get; set; }

    public GenerateDiscrepancyPdfQuery(Guid receiptId)
    {
        ReceiptId = receiptId;
    }
}

public class GenerateDiscrepancyPdfQueryHandler : IRequestHandler<GenerateDiscrepancyPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateDiscrepancyPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateDiscrepancyPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _context.WarehouseReceipts
            .Include(x => x.Lpns)
                .ThenInclude(l => l.Order)
                    .ThenInclude(o => o.OrderDimension)
            .Include(x => x.Order)
                .ThenInclude(o => o.OrderDimension)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x => x.ReceiptId == request.ReceiptId, cancellationToken);

        if (receipt == null)
            throw new Exception("Warehouse receipt not found");

        var discrepancyItems = receipt.Lpns
            .Where(x => x.State == ColdChainX.Core.Enums.LpnState.DISCREPANCY_HOLD || x.State == ColdChainX.Core.Enums.LpnState.RETURN_PENDING || x.Quantity != (x.Order?.Quantity ?? 0))
            .ToList();

        var data = new
        {
            CompanyName = "ColdChainX Logistics",
            HubName = receipt.Warehouse?.WarehouseName ?? "N/A",
            DiscrepancyCode = $"BBBT-{receipt.ReceiptCode}",
            Time = DateTime.Now.ToString("HH:mm"),
            Date = DateTime.Now.ToString("dd/MM/yyyy"),
            QcName = "",
            DriverName = receipt.DelivererName ?? "N/A",
            VehiclePlateNumber = "N/A", // Truck plate is usually on trip
            OrderCode = receipt.Order?.TrackingCode ?? "N/A",
            DiscrepancyItems = discrepancyItems.Select((item, index) => {
                var expectedQty = item.Order?.Quantity ?? 0;
                var actualQty = item.Quantity;
                var expectedWeight = item.Order?.OrderDimension?.ExpectedWeightKg ?? 0m;
                var actualWeight = item.ActualWeightKg;
                var expectedCbm = item.Order?.OrderDimension?.ExpectedCbm ?? 0m;
                var actualCbm = item.ActualCbm;
                var expectedLength = item.Order?.OrderDimension?.LengthCm ?? 0m;
                var actualLength = item.LengthCm ?? 0m;
                var expectedWidth = item.Order?.OrderDimension?.WidthCm ?? 0m;
                var actualWidth = item.WidthCm ?? 0m;
                var expectedHeight = item.Order?.OrderDimension?.HeightCm ?? 0m;
                var actualHeight = item.HeightCm ?? 0m;

                var isQtyDiff = expectedQty != actualQty;
                var isWeightDiff = Math.Abs(expectedWeight - actualWeight) > 0.01m;
                var isCbmDiff = Math.Abs(expectedCbm - actualCbm) > 0.0001m;
                var isLengthDiff = Math.Abs(expectedLength - actualLength) > 0.01m;
                var isWidthDiff = Math.Abs(expectedWidth - actualWidth) > 0.01m;
                var isHeightDiff = Math.Abs(expectedHeight - actualHeight) > 0.01m;

                Func<decimal, decimal, decimal> calculateDiff = (exp, act) => {
                    if (exp <= 0) return act > 0 ? 100m : 0m;
                    return Math.Round(Math.Abs(act - exp) / exp * 100m, 2);
                };

                return new
                {
                    Index = index + 1,
                    LpnCode = item.LpnCode,
                    ItemName = item.Order?.ItemName ?? "Unknown",
                    
                    ExpectedQty = expectedQty,
                    ActualQty = actualQty,
                    IsQtyDiff = isQtyDiff,
                    QtyDiffPercent = calculateDiff(expectedQty, actualQty).ToString("0.##"),

                    ExpectedWeight = expectedWeight.ToString("0.##"),
                    ActualWeight = actualWeight.ToString("0.##"),
                    IsWeightDiff = isWeightDiff,
                    WeightDiffPercent = calculateDiff(expectedWeight, actualWeight).ToString("0.##"),

                    ExpectedCbm = expectedCbm.ToString("0.####"),
                    ActualCbm = actualCbm.ToString("0.####"),
                    IsCbmDiff = isCbmDiff,
                    CbmDiffPercent = calculateDiff(expectedCbm, actualCbm).ToString("0.##"),

                    ExpectedLength = expectedLength.ToString("0.##"),
                    ActualLength = actualLength.ToString("0.##"),
                    IsLengthDiff = isLengthDiff,
                    LengthDiffPercent = calculateDiff(expectedLength, actualLength).ToString("0.##"),

                    ExpectedWidth = expectedWidth.ToString("0.##"),
                    ActualWidth = actualWidth.ToString("0.##"),
                    IsWidthDiff = isWidthDiff,
                    WidthDiffPercent = calculateDiff(expectedWidth, actualWidth).ToString("0.##"),

                    ExpectedHeight = expectedHeight.ToString("0.##"),
                    ActualHeight = actualHeight.ToString("0.##"),
                    IsHeightDiff = isHeightDiff,
                    HeightDiffPercent = calculateDiff(expectedHeight, actualHeight).ToString("0.##"),

                    Reason = item.DiscrepancyReason ?? item.State.ToString()
                };
            }),
            AdditionalNotes = receipt.Note ?? "Không có"
        };

        return await _pdfGenerator.GeneratePdfAsync("DiscrepancyReport", data);
    }
}
