using ColdChainX.Application.Helpers;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ReEvaluateInboundQcCommandHandler : IRequestHandler<ReEvaluateInboundQcCommand, ReEvaluateInboundQcResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ReEvaluateInboundQcCommandHandler> _logger;
    private readonly IFileService _fileService;

    public ReEvaluateInboundQcCommandHandler(
        IApplicationDbContext context,
        ILogger<ReEvaluateInboundQcCommandHandler> logger,
        IFileService fileService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
    }

    public async Task<ReEvaluateInboundQcResponse> Handle(
        ReEvaluateInboundQcCommand request,
        CancellationToken cancellationToken)
    {
        if (request.LpnId == Guid.Empty)
            return Failure("LpnId is required.");

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
        {
            return Failure("Actual weight and dimensions must be greater than 0 when Actual_Package_Lines is not provided.");
        }

        var lpn = await _context.Lpns
            .Include(l => l.Receipt)
            .Include(l => l.Order)
                .ThenInclude(o => o!.OrderDimension)
            .Include(l => l.Order)
                .ThenInclude(o => o!.Schedule)
            .Include(l => l.InboundQcPackageLines)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return Failure("LPN not found.");

        var order = lpn.Order;
        if (order == null)
            return Failure("Linked order not found.");

        if (order.OrderDimension == null)
            return Failure("The order dimensions were not found. QC re-evaluation cannot record actual measurements.");

        var asn = await _context.InboundAsns
            .FirstOrDefaultAsync(a => a.OrderId == lpn.OrderId, cancellationToken);
        if (hasActualPackageLines && asn == null)
            return Failure("ASN was not found. QC package lines require an ASN reference.");

        var preconditionFailure = ValidateEditableLpn(lpn, request.WarehouseId, asn?.WarehouseId);
        if (preconditionFailure != null)
            return Failure(preconditionFailure);

        var now = DbNow();
        var actualWeightKg = hasActualPackageLines
            ? actualPackageLines.Sum(line => line.ActualWeightKg)
            : request.ActualWeightKg!.Value;
        var actualQuantity = hasActualPackageLines
            ? actualPackageLines.Sum(line => line.Quantity)
            : Math.Max(1, lpn.Quantity);
        var actualCbm = hasActualPackageLines
            ? actualPackageLines.Sum(line => CalculateCbm(line.LengthCm, line.WidthCm, line.HeightCm, line.Quantity))
            : InboundQcMeasurementCalculator.CalculateCbm(
                request.LengthCm!.Value,
                request.WidthCm!.Value,
                request.HeightCm!.Value,
                Math.Max(1, lpn.Quantity));

#if DEBUG
        _logger.LogDebug(
            "Inbound QC re-evaluation actualCbm={ActualCbm} actualPackageLines={ActualPackageLinesCount}",
            actualCbm,
            actualPackageLines.Count);
#endif

        var finalQuotationResult = await UpsertFinalQuotationFromActualAsync(
            order,
            actualWeightKg,
            actualCbm,
            now,
            cancellationToken);
        if (finalQuotationResult.Error != null)
            return Failure(finalQuotationResult.Error);

        string? evidenceImageUrl = lpn.EvidenceImageUrl;
        if (request.EvidenceImages is { Count: > 0 })
        {
            var uploadedUrls = new List<string>();
            foreach (var file in request.EvidenceImages)
            {
                if (file.Length > 10 * 1024 * 1024)
                    return Failure($"File {file.FileName} exceeds the 10MB size limit.");

                uploadedUrls.Add(await _fileService.UploadFileAsync(file));
            }

            evidenceImageUrl = string.Join(",", uploadedUrls);
        }

        if (hasActualPackageLines)
        {
            _context.InboundQcPackageLines.RemoveRange(lpn.InboundQcPackageLines);

            foreach (var line in actualPackageLines)
            {
                _context.InboundQcPackageLines.Add(new InboundQcPackageLine
                {
                    InboundQcPackageLineId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    AsnId = asn!.AsnId,
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
        }

        lpn.Quantity = actualQuantity;
        lpn.ActualWeightKg = actualWeightKg;
        lpn.ActualCbm = actualCbm;
        lpn.LengthCm = hasActualPackageLines ? null : request.LengthCm!.Value;
        lpn.WidthCm = hasActualPackageLines ? null : request.WidthCm!.Value;
        lpn.HeightCm = hasActualPackageLines ? null : request.HeightCm!.Value;
        lpn.RecordedTemperature = request.Temperature;
        lpn.State = LpnState.RECEIVING;
        lpn.DiscrepancyReason = null;
        lpn.EvidenceImageUrl = evidenceImageUrl;
        lpn.UpdatedAt = now;

        order.OrderDimension.ActualWeightKg = actualWeightKg;
        order.OrderDimension.ActualCbm = actualCbm;
        order.Status = "RECEIVING";

        if (asn != null)
            asn.Status = "QC_PASSED";

        if (lpn.Receipt != null)
        {
            lpn.Receipt.ReferenceDocNo = "PENDING_PUTAWAY";
            lpn.Receipt.Reason = null;
            lpn.Receipt.TotalActualQty = actualQuantity;
            lpn.Receipt.RecordedTemperature = request.Temperature;
            lpn.Receipt.Note = "QC passed and waiting putaway. (Re-evaluated)";
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ReEvaluateInboundQcResponse
        {
            Success = true,
            Message = "Re-evaluation passed successfully. LPN ready for putaway.",
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            DiffPercent = CalculateMaxDiffPercent(order.OrderDimension.ExpectedWeightKg, order.OrderDimension.ExpectedCbm, actualWeightKg, actualCbm),
            PdfUrl = lpn.Receipt?.PdfUrl,
            ActualQuantity = actualQuantity,
            ActualWeightKg = actualWeightKg,
            ActualCbm = actualCbm,
            QuoteId = finalQuotationResult.QuoteId
        };
    }

    private static ReEvaluateInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static string? ValidateEditableLpn(Lpn lpn, Guid requestWarehouseId, Guid? asnWarehouseId)
    {
        if (lpn.State is LpnState.SHIPPING or LpnState.DELIVERED)
            return "Cannot re-evaluate QC after the LPN has started shipping or has been delivered.";

        if (requestWarehouseId != Guid.Empty)
        {
            if (asnWarehouseId.HasValue && asnWarehouseId.Value != requestWarehouseId)
                return "ASN does not belong to current warehouse.";

            if (lpn.WarehouseId.HasValue && lpn.WarehouseId.Value != requestWarehouseId)
                return "LPN does not belong to current warehouse.";
        }

        return null;
    }

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

    private async Task<(Guid? QuoteId, string? Error)> UpsertFinalQuotationFromActualAsync(
        TransportOrder order,
        decimal actualWeightKg,
        decimal actualCbm,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sourceQuotation = await _context.Quotations
            .Where(q => q.OrderId == order.OrderId && q.Status == "ACCEPTED")
            .OrderByDescending(q => q.AcceptedAt)
            .ThenByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceQuotation == null)
            return (null, "The accepted initial quotation was not found. QC re-evaluation cannot create the final quotation.");

        if (order.Schedule == null)
            return (null, "The order schedule was not found. QC re-evaluation cannot resolve final pricing.");

        var pricing = await ActualQuotationPricingHelper.ResolveAsync(
            _context,
            order.Schedule.RouteId,
            actualWeightKg,
            actualCbm,
            cancellationToken);
        if (!pricing.IsSuccess)
            return (null, pricing.Error);

        var finalQuotation = await _context.Quotations
            .Where(q => q.OrderId == order.OrderId
                && q.Status == "FINAL"
                && q.PricingSource == "AUTO_ACTUAL")
            .OrderByDescending(q => q.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var sourceSubtotal = sourceQuotation.FinalAmount - sourceQuotation.VatAmount;
        var finalSubtotal = sourceSubtotal - sourceQuotation.BaseFreight + pricing.BaseFreight;
        var vatPercentage = sourceQuotation.VatPercentage ?? 8m;
        var vatAmount = Math.Round(finalSubtotal * vatPercentage / 100m, 0);

        if (finalQuotation == null)
        {
            finalQuotation = new Quotation
            {
                QuoteId = Guid.NewGuid(),
                OrderId = order.OrderId,
                PricingSource = "AUTO_ACTUAL",
                Status = "FINAL",
                CreatedAt = now
            };
            _context.Quotations.Add(finalQuotation);
        }

        finalQuotation.BaseFreight = pricing.BaseFreight;
        finalQuotation.LastMileSurcharge = sourceQuotation.LastMileSurcharge;
        finalQuotation.VasAmount = sourceQuotation.VasAmount;
        finalQuotation.VatPercentage = vatPercentage;
        finalQuotation.VatAmount = vatAmount;
        finalQuotation.FinalAmount = finalSubtotal + vatAmount;
        finalQuotation.ChargeableWeightKg = pricing.ChargeableWeightKg;
        finalQuotation.VolumetricWeightKg = pricing.VolumetricWeightKg;
        finalQuotation.PricePerKg = pricing.PricePerKg;
        finalQuotation.DistanceKm = sourceQuotation.DistanceKm;
        finalQuotation.SystemBaseFreight = pricing.BaseFreight;
        finalQuotation.ManualAdjustment = sourceQuotation.ManualAdjustment;
        finalQuotation.AdditionalCharges = sourceQuotation.AdditionalCharges;
        finalQuotation.MandatoryCharges = sourceQuotation.MandatoryCharges;
        finalQuotation.OptionalServicesMenu = sourceQuotation.OptionalServicesMenu;
        finalQuotation.OverrideReason = sourceQuotation.OverrideReason;

        return (finalQuotation.QuoteId, null);
    }

    private static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * quantity / 1_000_000m, 4);

    private static decimal CalculateMaxDiffPercent(decimal expectedWeightKg, decimal expectedCbm, decimal actualWeightKg, decimal actualCbm)
    {
        var weightDiff = CalculateDiffPercentOrZero(expectedWeightKg, actualWeightKg);
        var cbmDiff = CalculateDiffPercentOrZero(expectedCbm, actualCbm);
        return Math.Max(weightDiff, cbmDiff);
    }

    private static decimal CalculateDiffPercentOrZero(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
