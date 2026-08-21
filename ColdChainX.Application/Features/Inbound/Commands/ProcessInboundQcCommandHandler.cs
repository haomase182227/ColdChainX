using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Helpers;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundQcCommandHandler : IRequestHandler<ProcessInboundQcCommand, ProcessInboundQcResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ProcessInboundQcCommandHandler> _logger;
    private readonly IFileService _fileService;

    public ProcessInboundQcCommandHandler(
        IApplicationDbContext context,
        ILogger<ProcessInboundQcCommandHandler> logger,
        IFileService fileService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
    }

    public async Task<ProcessInboundQcResponse> Handle(ProcessInboundQcCommand request, CancellationToken cancellationToken)
    {
        if (request.AsnId == Guid.Empty)
            return Failure("AsnId is required.");

        if (request.ReceiverId == Guid.Empty)
            return Failure("ReceiverId is required.");

        var packageLinesResult = ParseActualPackageLines(request.ActualPackageLinesJson);
        if (!packageLinesResult.Success)
            return Failure(packageLinesResult.Error!);

        var actualPackageLines = packageLinesResult.PackageLines;
        var hasActualPackageLines = actualPackageLines.Count > 0;

        if (!hasActualPackageLines
            && (!request.ActualWeightKg.HasValue
                || !request.LengthCm.HasValue
                || !request.WidthCm.HasValue
                || !request.HeightCm.HasValue
                || request.ActualWeightKg.Value <= 0
                || request.LengthCm.Value <= 0
                || request.WidthCm.Value <= 0
                || request.HeightCm.Value <= 0))
            return Failure("Actual weight and dimensions must be greater than 0 when Actual_Package_Lines is not provided.");

        var receiver = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.ReceiverId, cancellationToken);

        if (receiver == null)
            return Failure("Receiver user was not found.");

        var asn = await _context.InboundAsns
            .Include(a => a.Order)
                .ThenInclude(o => o.OrderDimension)
            .FirstOrDefaultAsync(a => a.AsnId == request.AsnId, cancellationToken);

        if (asn?.Order == null)
            return Failure("ASN or linked order was not found.");

        var warehouseId = request.WarehouseId != Guid.Empty
            ? request.WarehouseId
            : (receiver.WarehouseId ?? asn.WarehouseId);

        if (!warehouseId.HasValue || warehouseId.Value == Guid.Empty)
            return Failure("WarehouseId is required and could not be determined.");

        if (receiver.WarehouseId.HasValue && receiver.WarehouseId.Value != Guid.Empty && asn.WarehouseId.HasValue && asn.WarehouseId.Value != warehouseId.Value)
            return Failure("ASN does not belong to current receiver warehouse.");

        var uploadedUrls = new List<string>();
        if (request.EvidenceImages != null && request.EvidenceImages.Any())
        {
            foreach (var file in request.EvidenceImages)
            {
                if (file.Length > 10 * 1024 * 1024)
                {
                    return Failure($"File {file.FileName} exceeds the 10MB size limit.");
                }

                var url = await _fileService.UploadFileAsync(file);
                uploadedUrls.Add(url);
            }
        }
        var evidenceImageUrl = uploadedUrls.Any() ? string.Join(",", uploadedUrls) : null;

        if (asn.WarehouseId.HasValue && asn.WarehouseId.Value != warehouseId.Value)
            return Failure("ASN does not belong to current receiver warehouse.");

        var order = asn.Order;
        var now = DbNow();
        var actualWeightKg = hasActualPackageLines
            ? actualPackageLines.Sum(line => line.ActualWeightKg)
            : request.ActualWeightKg!.Value;
        var actualQuantity = hasActualPackageLines
            ? actualPackageLines.Sum(line => line.Quantity)
            : order.Quantity;
        var actualCbm = hasActualPackageLines
            ? actualPackageLines.Sum(line => CalculateCbm(line.LengthCm, line.WidthCm, line.HeightCm, line.Quantity))
            : InboundQcMeasurementCalculator.CalculateCbm(request.LengthCm!.Value, request.WidthCm!.Value, request.HeightCm!.Value, order.Quantity);

#if DEBUG
        _logger.LogDebug(
            "Inbound QC CBM trace storedExpectedCbm={StoredExpectedCbm} actualCbm={ActualCbm} actualPackageLines={ActualPackageLinesCount}",
            order.OrderDimension.ExpectedCbm,
            actualCbm,
            actualPackageLines.Count);
#endif

        var existingLpn = await _context.Lpns
            .AsNoTracking()
            .Where(l => l.OrderId == order.OrderId)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingLpn != null)
            return Failure($"Order already has LPN {existingLpn.LpnCode}. Use putaway flow instead.");

        var receipt = await _context.WarehouseReceipts
            .FirstOrDefaultAsync(r => r.OrderId == order.OrderId
                                      && r.WarehouseId == warehouseId.Value
                                      && r.ReferenceDocNo != "COMPLETED",
                cancellationToken);

        if (receipt == null)
        {
            receipt = new WarehouseReceipt
            {
                ReceiptId = Guid.NewGuid(),
                ReceiptCode = GenerateCode("REC"),
                ReferenceDocNo = "PENDING_PUTAWAY",
                OrderId = order.OrderId,
                WarehouseId = warehouseId.Value,
                ReceiptType = "INBOUND",
                Reason = null,
                TotalExpectedQty = order.Quantity,
                TotalActualQty = actualQuantity,
                RecordedTemperature = request.Temperature,
                DelivererName = "",
                ReceiverId = request.ReceiverId,
                Note = "Generated during QC.",
                CreatedAt = now
            };

            _context.WarehouseReceipts.Add(receipt);
        }
        else
        {
            receipt.ReferenceDocNo = "PENDING_PUTAWAY";
            receipt.RecordedTemperature = request.Temperature;
            receipt.TotalExpectedQty = order.Quantity;
            receipt.TotalActualQty = actualQuantity;
            receipt.Note = "QC passed and waiting putaway.";
        }

        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = GenerateCode("LPN"),
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            ReceiptId = receipt.ReceiptId,
            TripId = order.MasterTripId,
            Quantity = actualQuantity,
            ActualWeightKg = actualWeightKg,
            ActualCbm = actualCbm,
            LengthCm = hasActualPackageLines ? null : request.LengthCm!.Value,
            WidthCm = hasActualPackageLines ? null : request.WidthCm!.Value,
            HeightCm = hasActualPackageLines ? null : request.HeightCm!.Value,
            RequiredTemperature = ParseTemperature(order.TempCondition),
            RecordedTemperature = request.Temperature,
            State = LpnState.RECEIVING,
            DiscrepancyReason = null,
            EvidenceImageUrl = evidenceImageUrl,
            SlaDeadline = now.AddHours(24),
            CreatedAt = now
        };

        _context.Lpns.Add(lpn);

        foreach (var line in actualPackageLines)
        {
            _context.InboundQcPackageLines.Add(new InboundQcPackageLine
            {
                InboundQcPackageLineId = Guid.NewGuid(),
                OrderId = order.OrderId,
                AsnId = asn.AsnId,
                LpnId = lpn.LpnId,
                Label = string.IsNullOrWhiteSpace(line.Label) ? "Package" : line.Label.Trim(),
                Quantity = line.Quantity,
                ActualWeightKg = line.ActualWeightKg,
                LengthCm = line.LengthCm,
                WidthCm = line.WidthCm,
                HeightCm = line.HeightCm,
                ActualCbm = CalculateCbm(line.LengthCm, line.WidthCm, line.HeightCm, line.Quantity),
                CreatedAt = now
            });
        }

        asn.Status = "QC_PASSED";
        if (order.OrderDimension != null)
        {
            order.OrderDimension.ActualWeightKg = actualWeightKg;
            order.OrderDimension.ActualCbm = actualCbm;
        }
        order.Status = "RECEIVING";

        await CreateFinalQuotationFromActualAsync(order.OrderId, actualWeightKg, actualCbm, now, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return new ProcessInboundQcResponse
        {
            Success = true,
            Message = "QC passed successfully. LPN ready for putaway.",
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            ReceiptId = receipt?.ReceiptId,
            DiffPercent = 0m
        };
    }

    private static ProcessInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static (bool Success, List<InboundQcPackageLineRequest> PackageLines, string? Error) ParseActualPackageLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (true, new List<InboundQcPackageLineRequest>(), null);

        try
        {
            var lines = JsonSerializer.Deserialize<List<InboundQcPackageLineRequest>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InboundQcPackageLineRequest>();

            foreach (var line in lines)
            {
                if (line.Quantity <= 0)
                    return (false, lines, "Each actual package line quantity must be greater than 0.");

                if (line.ActualWeightKg <= 0)
                    return (false, lines, "Each actual package line weight must be greater than 0.");

                if (line.LengthCm <= 0 || line.WidthCm <= 0 || line.HeightCm <= 0)
                    return (false, lines, "Each actual package line length, width, and height must be greater than 0.");
            }

            return (true, lines, null);
        }
        catch (JsonException)
        {
            return (false, new List<InboundQcPackageLineRequest>(), "Actual_Package_Lines must be valid JSON.");
        }
    }

    private static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * quantity / 1_000_000m, 4);

    private async Task CreateFinalQuotationFromActualAsync(
        Guid orderId,
        decimal actualWeightKg,
        decimal actualCbm,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingFinal = await _context.Quotations
            .AnyAsync(q => q.OrderId == orderId
                && q.Status == "FINAL"
                && q.PricingSource == "AUTO_ACTUAL", cancellationToken);

        if (existingFinal)
            return;

        var sourceQuotation = await _context.Quotations
            .Where(q => q.OrderId == orderId)
            .OrderByDescending(q => q.Status == "ACCEPTED")
            .ThenByDescending(q => q.AcceptedAt)
            .ThenByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceQuotation == null || !sourceQuotation.PricePerKg.HasValue)
            return;

        const decimal minChargeableWeightKg = 30m;
        const decimal defaultVolumetricConversionRate = 250m;
        var volumetricWeight = Math.Round(actualCbm * defaultVolumetricConversionRate, 2);
        var chargeableWeight = Math.Max(Math.Max(actualWeightKg, volumetricWeight), minChargeableWeightKg);
        var baseFreight = Math.Round(chargeableWeight * sourceQuotation.PricePerKg.Value, 0);

        var sourceSubtotal = sourceQuotation.FinalAmount - sourceQuotation.VatAmount;
        var finalSubtotal = sourceSubtotal - sourceQuotation.BaseFreight + baseFreight;
        var vatPercentage = sourceQuotation.VatPercentage ?? 8m;
        var vatAmount = Math.Round(finalSubtotal * vatPercentage / 100m, 0);

        _context.Quotations.Add(new Quotation
        {
            QuoteId = Guid.NewGuid(),
            OrderId = orderId,
            BaseFreight = baseFreight,
            LastMileSurcharge = sourceQuotation.LastMileSurcharge,
            VasAmount = sourceQuotation.VasAmount,
            VatPercentage = vatPercentage,
            VatAmount = vatAmount,
            FinalAmount = finalSubtotal + vatAmount,
            ChargeableWeightKg = chargeableWeight,
            VolumetricWeightKg = volumetricWeight,
            PricePerKg = sourceQuotation.PricePerKg,
            DistanceKm = sourceQuotation.DistanceKm,
            SystemBaseFreight = baseFreight,
            ManualAdjustment = sourceQuotation.ManualAdjustment,
            AdditionalCharges = sourceQuotation.AdditionalCharges,
            MandatoryCharges = sourceQuotation.MandatoryCharges,
            OptionalServicesMenu = sourceQuotation.OptionalServicesMenu,
            OverrideReason = sourceQuotation.OverrideReason,
            PricingSource = "AUTO_ACTUAL",
            Status = "FINAL",
            CreatedAt = now
        });
    }

    private static ProductCategory ParseProductCategory(string? value)
        => Enum.TryParse<ProductCategory>(NormalizeCategory(value), true, out var category)
            ? category
            : ProductCategory.FOOD;

    private static string NormalizeCategory(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace(" ", "_")
            .Replace("-", "_")
            .ToUpperInvariant();

    private static decimal? ParseTemperature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = new string(value
                .Where(ch => char.IsDigit(ch) || ch == '-' || ch == '.' || ch == ',')
                .ToArray())
            .Replace(',', '.')
            .Trim();

        return decimal.TryParse(normalized, out var temp) ? temp : null;
    }

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static string GenerateCode(string prefix)
        => $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
}
