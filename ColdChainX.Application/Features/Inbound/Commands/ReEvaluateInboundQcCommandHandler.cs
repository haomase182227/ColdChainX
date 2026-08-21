using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Helpers;
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

        var hasPackageLines = packageLinesResult.PackageLines.Count > 0;
        if (!hasPackageLines && !HasValidLegacyMeasurements(request))
            return Failure("Actual_Package_Lines or positive legacy weight and dimensions are required.");

        var currentLpn = await _context.Lpns
            .AsNoTracking()
            .Include(lpn => lpn.Receipt)
            .FirstOrDefaultAsync(lpn => lpn.LpnId == request.LpnId, cancellationToken);

        var preconditionFailure = ValidateEditableLpn(currentLpn, request.WarehouseId);
        if (preconditionFailure != null)
            return Failure(preconditionFailure);

        string? evidenceImageUrl = null;
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

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var lpn = await _context.Lpns
                    .Include(entity => entity.Receipt)
                    .Include(entity => entity.InboundQcPackageLines)
                    .Include(entity => entity.Order)
                        .ThenInclude(order => order.OrderDimension)
                    .Include(entity => entity.Order)
                        .ThenInclude(order => order.Schedule)
                    .FirstOrDefaultAsync(entity => entity.LpnId == request.LpnId, cancellationToken);

                var editableFailure = ValidateEditableLpn(lpn, request.WarehouseId);
                if (editableFailure != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure(editableFailure);
                }

                if (lpn!.Order?.OrderDimension == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("Linked order measurements were not found.");
                }

                var asn = await _context.InboundAsns
                    .FirstOrDefaultAsync(entity => entity.OrderId == lpn.OrderId, cancellationToken);
                if (asn == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("Linked ASN was not found.");
                }

                if (asn.WarehouseId.HasValue
                    && request.WarehouseId != Guid.Empty
                    && asn.WarehouseId.Value != request.WarehouseId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("ASN does not belong to current warehouse.");
                }

                var finalQuotations = await _context.Quotations
                    .Where(quotation => quotation.OrderId == lpn.OrderId
                        && quotation.Status == "FINAL"
                        && quotation.PricingSource == "AUTO_ACTUAL")
                    .OrderByDescending(quotation => quotation.CreatedAt)
                    .ToListAsync(cancellationToken);

                if (finalQuotations.Count == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("The FINAL/AUTO_ACTUAL quotation was not found.");
                }

                if (finalQuotations.Count > 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("Multiple FINAL/AUTO_ACTUAL quotations were found. Resolve duplicates before correcting QC.");
                }

                var finalQuotation = finalQuotations[0];
                if (!finalQuotation.PricePerKg.HasValue || finalQuotation.PricePerKg.Value <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("The final quotation does not have a valid price per kg.");
                }

                var correctedLines = hasPackageLines
                    ? packageLinesResult.PackageLines
                    : CreateLegacyPackageLine(request, lpn.Quantity);
                var actualQuantity = correctedLines.Sum(line => line.Quantity);
                var actualWeightKg = correctedLines.Sum(line => line.ActualWeightKg);
                var actualCbm = correctedLines.Sum(line =>
                    CalculateCbm(line.LengthCm, line.WidthCm, line.HeightCm, line.Quantity));
                var now = DbNow();

                _context.InboundQcPackageLines.RemoveRange(lpn.InboundQcPackageLines);
                foreach (var line in correctedLines)
                {
                    _context.InboundQcPackageLines.Add(new InboundQcPackageLine
                    {
                        InboundQcPackageLineId = Guid.NewGuid(),
                        OrderId = lpn.OrderId,
                        AsnId = asn.AsnId,
                        LpnId = lpn.LpnId,
                        Label = string.IsNullOrWhiteSpace(line.Label) ? "Kiện hàng" : line.Label.Trim(),
                        Quantity = line.Quantity,
                        ActualWeightKg = line.ActualWeightKg,
                        LengthCm = line.LengthCm,
                        WidthCm = line.WidthCm,
                        HeightCm = line.HeightCm,
                        ActualCbm = CalculateCbm(line.LengthCm, line.WidthCm, line.HeightCm, line.Quantity),
                        CreatedAt = now
                    });
                }

                lpn.Quantity = actualQuantity;
                lpn.ActualWeightKg = actualWeightKg;
                lpn.ActualCbm = actualCbm;
                lpn.LengthCm = hasPackageLines ? null : request.LengthCm;
                lpn.WidthCm = hasPackageLines ? null : request.WidthCm;
                lpn.HeightCm = hasPackageLines ? null : request.HeightCm;
                lpn.RecordedTemperature = request.Temperature;
                lpn.DiscrepancyReason = null;
                lpn.UpdatedAt = now;
                if (evidenceImageUrl != null)
                    lpn.EvidenceImageUrl = evidenceImageUrl;

                lpn.Order.OrderDimension.ActualWeightKg = actualWeightKg;
                lpn.Order.OrderDimension.ActualCbm = actualCbm;
                lpn.Order.Status = "RECEIVING";

                asn.Status = "QC_PASSED";

                lpn.Receipt.ReferenceDocNo = "PENDING_PUTAWAY";
                lpn.Receipt.TotalExpectedQty = lpn.Order.Quantity;
                lpn.Receipt.TotalActualQty = actualQuantity;
                lpn.Receipt.RecordedTemperature = request.Temperature;
                lpn.Receipt.Reason = null;
                lpn.Receipt.Note = "Đã cập nhật lại số đo kiểm kê, chờ xếp kho.";

                if (lpn.Order.Schedule == null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("The order schedule was not found. Final quotation cannot be recalculated.");
                }

                var pricing = await ActualQuotationPricingHelper.ResolveAsync(
                    _context,
                    lpn.Order.Schedule.RouteId,
                    actualWeightKg,
                    actualCbm,
                    cancellationToken);
                if (!pricing.IsSuccess)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure(pricing.Error!);
                }

                UpdateFinalQuotation(finalQuotation, pricing);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new ReEvaluateInboundQcResponse
                {
                    Success = true,
                    Message = "QC measurements and final quotation were updated successfully.",
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode,
                    State = lpn.State.ToString(),
                    DiffPercent = 0m,
                    ActualQuantity = actualQuantity,
                    ActualWeightKg = actualWeightKg,
                    ActualCbm = actualCbm,
                    QuoteId = finalQuotation.QuoteId
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to correct inbound QC measurements for LPN {LpnId}.", request.LpnId);
                return Failure("QC correction failed. No database changes were saved.");
            }
        });
    }

    private static string? ValidateEditableLpn(Lpn? lpn, Guid warehouseId)
    {
        if (lpn == null)
            return "LPN not found.";

        if (lpn.State != LpnState.RECEIVING)
            return $"Only LPNs in RECEIVING state can be corrected. Current state: {lpn.State}.";

        if (lpn.Receipt == null)
            return "Warehouse receipt was not found for this LPN.";

        if (!string.IsNullOrWhiteSpace(lpn.Receipt.PdfUrl))
            return "QC measurements cannot be corrected after the warehouse receipt PDF has been generated.";

        if (warehouseId != Guid.Empty && lpn.Receipt.WarehouseId != warehouseId)
            return "LPN does not belong to current warehouse.";

        return null;
    }

    private static bool HasValidLegacyMeasurements(ReEvaluateInboundQcCommand request)
        => request.ActualWeightKg > 0
            && request.LengthCm > 0
            && request.WidthCm > 0
            && request.HeightCm > 0;

    private static List<InboundQcPackageLineRequest> CreateLegacyPackageLine(
        ReEvaluateInboundQcCommand request,
        int quantity)
        => new()
        {
            new InboundQcPackageLineRequest
            {
                Label = "Kiện hàng",
                Quantity = Math.Max(quantity, 1),
                ActualWeightKg = request.ActualWeightKg!.Value,
                LengthCm = request.LengthCm!.Value,
                WidthCm = request.WidthCm!.Value,
                HeightCm = request.HeightCm!.Value
            }
        };

    private static (bool Success, List<InboundQcPackageLineRequest> PackageLines, string? Error)
        ParseActualPackageLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (true, new List<InboundQcPackageLineRequest>(), null);

        try
        {
            var lines = JsonSerializer.Deserialize<List<InboundQcPackageLineRequest>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<InboundQcPackageLineRequest>();

            if (lines.Count == 0)
                return (false, lines, "Actual_Package_Lines must contain at least one item.");

            foreach (var line in lines)
            {
                if (line.Quantity <= 0)
                    return (false, lines, "Each actual package line quantity must be greater than 0.");
                if (line.ActualWeightKg <= 0)
                    return (false, lines, "Each actual package line weight must be greater than 0.");
                if (line.LengthCm <= 0 || line.WidthCm <= 0 || line.HeightCm <= 0)
                    return (false, lines, "Each actual package line dimension must be greater than 0.");
            }

            return (true, lines, null);
        }
        catch (JsonException)
        {
            return (false, new List<InboundQcPackageLineRequest>(), "Actual_Package_Lines must be valid JSON.");
        }
    }

    private static decimal CalculateCbm(
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm,
        int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * quantity / 1_000_000m, 4);

    private static void UpdateFinalQuotation(
        Quotation quotation,
        ActualQuotationPricingResult pricing)
    {
        var previousSubtotal = quotation.FinalAmount - quotation.VatAmount;
        var correctedSubtotal = previousSubtotal - quotation.BaseFreight + pricing.BaseFreight;
        var vatPercentage = quotation.VatPercentage ?? 8m;
        var vatAmount = Math.Round(correctedSubtotal * vatPercentage / 100m, 0);

        quotation.BaseFreight = pricing.BaseFreight;
        quotation.SystemBaseFreight = pricing.BaseFreight;
        quotation.ChargeableWeightKg = pricing.ChargeableWeightKg;
        quotation.VolumetricWeightKg = pricing.VolumetricWeightKg;
        quotation.PricePerKg = pricing.PricePerKg;
        quotation.VatPercentage = vatPercentage;
        quotation.VatAmount = vatAmount;
        quotation.FinalAmount = correctedSubtotal + vatAmount;
        quotation.FileUrl = null;
    }

    private static ReEvaluateInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
