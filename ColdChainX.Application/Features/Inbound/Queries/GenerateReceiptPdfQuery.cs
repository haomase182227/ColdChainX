using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GenerateReceiptPdfQuery : IRequest<byte[]>
{
    public Guid ReceiptId { get; set; }

    public GenerateReceiptPdfQuery(Guid receiptId)
    {
        ReceiptId = receiptId;
    }
}

public class GenerateReceiptPdfQueryHandler : IRequestHandler<GenerateReceiptPdfQuery, byte[]>
{
    private readonly IApplicationDbContext _context;
    private readonly IPdfGeneratorService _pdfGenerator;

    public GenerateReceiptPdfQueryHandler(IApplicationDbContext context, IPdfGeneratorService pdfGenerator)
    {
        _context = context;
        _pdfGenerator = pdfGenerator;
    }

    public async Task<byte[]> Handle(GenerateReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await _context.WarehouseReceipts
            .Include(x => x.Lpns)
                .ThenInclude(l => l.Order)
            .Include(x => x.Lpns)
                .ThenInclude(l => l.InboundQcPackageLines)
            .Include(x => x.Order)
                .ThenInclude(o => o.Customer)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x => x.ReceiptId == request.ReceiptId, cancellationToken);

        if (receipt == null)
            throw new Exception("Warehouse receipt not found");

        var receiptLines = receipt.Lpns
            .SelectMany(lpn => lpn.InboundQcPackageLines.Count > 0
                ? lpn.InboundQcPackageLines.Select(line => new ReceiptLine(
                    BuildItemDescription(lpn.Order?.ItemName, line.Label),
                    lpn.LpnCode,
                    line.Quantity,
                    line.ActualWeightKg,
                    line.LengthCm,
                    line.WidthCm,
                    line.HeightCm,
                    line.ActualCbm,
                    line.CreatedAt))
                : new[]
                {
                    new ReceiptLine(
                        BuildItemDescription(lpn.Order?.ItemName, null),
                        lpn.LpnCode,
                        lpn.Quantity,
                        lpn.ActualWeightKg,
                        lpn.LengthCm,
                        lpn.WidthCm,
                        lpn.HeightCm,
                        lpn.ActualCbm,
                        lpn.CreatedAt)
                })
            .OrderBy(line => line.CreatedAt)
            .ThenBy(line => line.ItemDescription)
            .ToList();

        var data = new
        {
            CompanyName = "ColdChainX Logistics",
            HubName = receipt.Warehouse?.WarehouseName ?? "Không xác định",
            HubAddress = receipt.Warehouse?.Address ?? "Không có",
            CreatedDay = receipt.CreatedAt?.Day.ToString("00") ?? DateTime.Now.Day.ToString("00"),
            CreatedMonth = receipt.CreatedAt?.Month.ToString("00") ?? DateTime.Now.Month.ToString("00"),
            CreatedYear = receipt.CreatedAt?.Year.ToString() ?? DateTime.Now.Year.ToString(),
            ReceiptCode = receipt.ReceiptCode,
            DriverName = string.IsNullOrWhiteSpace(receipt.DelivererName) ? "Chưa cập nhật" : receipt.DelivererName,
            OrderCode = receipt.Order?.TrackingCode ?? "Không xác định",
            CustomerName = receipt.Order?.Customer?.CompanyName ?? "Không xác định",
            RecordedTemperature = receipt.RecordedTemperature.HasValue
                ? $"{receipt.RecordedTemperature.Value.ToString("0.##", CultureInfo.InvariantCulture)} °C"
                : "Không ghi nhận",
            ReceiptStatus = receipt.Lpns.Count > 0
                ? string.Join(", ", receipt.Lpns.Select(lpn => TranslateState(lpn.State)).Distinct())
                : "Chưa có hàng",
            CreatorName = "", 
            WarehouseManagerName = "",
            PackageLines = receiptLines.Select((line, index) => new
            {
                Index = index + 1,
                line.ItemDescription,
                line.LpnCode,
                Quantity = line.Quantity,
                ActualWeightKg = line.ActualWeightKg.ToString("0.##", CultureInfo.InvariantCulture),
                Dimensions = FormatDimensions(line.LengthCm, line.WidthCm, line.HeightCm),
                ActualCbm = line.ActualCbm.ToString("0.####", CultureInfo.InvariantCulture)
            }),
            TotalPackageQuantity = receiptLines.Sum(line => line.Quantity),
            TotalActualWeightKg = receiptLines.Sum(line => line.ActualWeightKg).ToString("0.##", CultureInfo.InvariantCulture),
            TotalActualCbm = receiptLines.Sum(line => line.ActualCbm).ToString("0.####", CultureInfo.InvariantCulture)
        };

        return await _pdfGenerator.GeneratePdfAsync("WarehouseReceipt", data);
    }

    private static string TranslateState(LpnState state)
        => state switch
        {
            LpnState.RECEIVING => "Chờ xếp kho",
            LpnState.IN_STOCK => "Đã nhập kho",
            LpnState.DISCREPANCY_HOLD => "Tạm giữ để kiểm tra",
            _ => "Đang xử lý"
        };

    private static string BuildItemDescription(string? itemName, string? packageLabel)
    {
        var normalizedItemName = string.IsNullOrWhiteSpace(itemName) ? "Hàng hóa" : itemName.Trim();
        return string.IsNullOrWhiteSpace(packageLabel)
            ? normalizedItemName
            : $"{normalizedItemName} - {packageLabel.Trim()}";
    }

    private static string FormatDimensions(decimal? lengthCm, decimal? widthCm, decimal? heightCm)
    {
        if (!lengthCm.HasValue || !widthCm.HasValue || !heightCm.HasValue
            || lengthCm.Value <= 0 || widthCm.Value <= 0 || heightCm.Value <= 0)
            return "Không ghi nhận";

        return $"{lengthCm.Value.ToString("0.##", CultureInfo.InvariantCulture)} × {widthCm.Value.ToString("0.##", CultureInfo.InvariantCulture)} × {heightCm.Value.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    private sealed record ReceiptLine(
        string ItemDescription,
        string LpnCode,
        int Quantity,
        decimal ActualWeightKg,
        decimal? LengthCm,
        decimal? WidthCm,
        decimal? HeightCm,
        decimal ActualCbm,
        DateTime? CreatedAt);
}
