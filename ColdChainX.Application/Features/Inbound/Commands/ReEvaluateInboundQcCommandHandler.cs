using ColdChainX.Application.Features.Discrepancy.Queries;
using ColdChainX.Application.Features.Inbound.Queries;
using ColdChainX.Application.Helpers;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ReEvaluateInboundQcCommandHandler : IRequestHandler<ReEvaluateInboundQcCommand, ReEvaluateInboundQcResponse>
{
    private const decimal DiscrepancyThresholdPercent = 5m;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ReEvaluateInboundQcCommandHandler> _logger;
    private readonly IFileService _fileService;
    private readonly IMediator _mediator;

    public ReEvaluateInboundQcCommandHandler(
        IApplicationDbContext context,
        ILogger<ReEvaluateInboundQcCommandHandler> logger,
        IFileService fileService,
        IMediator mediator)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
        _mediator = mediator;
    }

    public async Task<ReEvaluateInboundQcResponse> Handle(ReEvaluateInboundQcCommand request, CancellationToken cancellationToken)
    {
        if (request.LpnId == Guid.Empty)
            return Failure("LpnId is required.");

        if (request.PackageMeasurements.Count == 0
            && (request.ActualWeightKg <= 0 || request.LengthCm <= 0 || request.WidthCm <= 0 || request.HeightCm <= 0))
            return Failure("Actual weight and dimensions must be greater than 0.");

        var lpn = await _context.Lpns
            .Include(l => l.Receipt)
            .Include(l => l.Route) // for tracking
            .Include(l => l.Trip)
            .Include(l => l.PackageVariantLines)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return Failure("LPN not found.");

        if (lpn.State != LpnState.DISCREPANCY_HOLD)
            return Failure("LPN is not in DISCREPANCY_HOLD state. Cannot re-evaluate.");

        var order = await _context.TransportOrders
            .Include(o => o.OrderDimension)
            .FirstOrDefaultAsync(o => o.OrderId == lpn.OrderId, cancellationToken);

        if (order == null)
            return Failure("Linked order not found.");

        if (order.OrderDimension == null)
            return Failure("Expected order measurements were not found. QC re-evaluation cannot be calculated.");

        if (order.OrderDimension.ExpectedWeightKg <= 0
            || order.OrderDimension.LengthCm <= 0
            || order.OrderDimension.WidthCm <= 0
            || order.OrderDimension.HeightCm <= 0)
            return Failure("Expected weight and dimensions must be greater than 0 before QC re-evaluation.");

        var asn = await _context.InboundAsns
            .FirstOrDefaultAsync(a => a.OrderId == lpn.OrderId, cancellationToken);

        if (asn != null && asn.WarehouseId.HasValue && request.WarehouseId != Guid.Empty && asn.WarehouseId.Value != request.WarehouseId)
            return Failure("ASN does not belong to current warehouse.");

        var receipt = lpn.Receipt;
        if (receipt == null)
            return Failure("Warehouse Receipt not found for this LPN.");

        var now = DateTime.UtcNow;
        decimal maxDiff;
        bool hasDiscrepancy;

        if (lpn.PackageVariantLines.Count > 0)
        {
            List<PackageVariantQcMeasurement> measurements;
            if (request.PackageMeasurements.Count == 0)
            {
                if (lpn.PackageVariantLines.Count != 1)
                    return Failure("PackageMeasurements must contain one measurement for every size in this LPN.");

                var line = lpn.PackageVariantLines.Single();
                measurements = new List<PackageVariantQcMeasurement>
                {
                    new()
                    {
                        OrderPackageVariantId = line.OrderPackageVariantId ?? Guid.Empty,
                        Quantity = line.Quantity,
                        ActualWeightKg = request.ActualWeightKg,
                        LengthCm = request.LengthCm,
                        WidthCm = request.WidthCm,
                        HeightCm = request.HeightCm,
                        Temperature = request.Temperature,
                        EvidenceImages = request.EvidenceImages ?? new List<Microsoft.AspNetCore.Http.IFormFile>()
                    }
                };
            }
            else
            {
                measurements = request.PackageMeasurements;
            }

            var lineByVariantId = lpn.PackageVariantLines
                .Where(line => line.OrderPackageVariantId.HasValue)
                .ToDictionary(line => line.OrderPackageVariantId!.Value);
            if (measurements.Count != lpn.PackageVariantLines.Count
                || measurements.Select(measurement => measurement.OrderPackageVariantId).Distinct().Count() != measurements.Count)
            {
                return Failure("PackageMeasurements must contain each size in this LPN exactly once.");
            }

            foreach (var measurement in measurements)
            {
                LpnPackageVariantLine? line = null;
                if (measurement.OrderPackageVariantId != Guid.Empty)
                    lineByVariantId.TryGetValue(measurement.OrderPackageVariantId, out line);
                else if (lpn.PackageVariantLines.Count == 1 && !lpn.PackageVariantLines.Single().OrderPackageVariantId.HasValue)
                    line = lpn.PackageVariantLines.Single();

                if (line == null)
                    return Failure($"Package size {measurement.OrderPackageVariantId} does not belong to this LPN.");
                if (measurement.Quantity > 0 && measurement.Quantity != line.Quantity)
                    return Failure($"Package size {measurement.OrderPackageVariantId} must keep quantity {line.Quantity} during re-evaluation.");
                if (measurement.ActualWeightKg <= 0 || measurement.LengthCm <= 0 || measurement.WidthCm <= 0 || measurement.HeightCm <= 0)
                    return Failure("Actual weight and dimensions must be greater than 0 for every package size.");

                var uploadedUrls = new List<string>();
                foreach (var file in measurement.EvidenceImages)
                {
                    if (file.Length <= 0 || file.Length > 10 * 1024 * 1024)
                        return Failure($"File {file.FileName} must be non-empty and cannot exceed 10MB.");
                    uploadedUrls.Add(await _fileService.UploadFileAsync(file));
                }

                var actualLineCbm = InboundQcMeasurementCalculator.CalculateCbm(
                    measurement.LengthCm,
                    measurement.WidthCm,
                    measurement.HeightCm,
                    line.Quantity);
                var weightDiff = CalculateDiffPercent(line.ExpectedWeightKg, measurement.ActualWeightKg);
                var cbmDiff = CalculateDiffPercent(line.ExpectedCbm, actualLineCbm);
                line.ActualWeightKg = measurement.ActualWeightKg;
                line.ActualCbm = actualLineCbm;
                line.LengthCm = measurement.LengthCm;
                line.WidthCm = measurement.WidthCm;
                line.HeightCm = measurement.HeightCm;
                line.RecordedTemperature = measurement.Temperature;
                line.DiffPercent = Math.Max(weightDiff, cbmDiff);
                line.HasDiscrepancy = line.DiffPercent > DiscrepancyThresholdPercent;
                if (uploadedUrls.Count > 0)
                    line.EvidenceImageUrl = string.Join(";", uploadedUrls);
                line.UpdatedAt = now;
            }

            lpn.Quantity = lpn.PackageVariantLines.Sum(line => line.Quantity);
            lpn.ActualWeightKg = lpn.PackageVariantLines.Sum(line => line.ActualWeightKg);
            lpn.ActualCbm = lpn.PackageVariantLines.Sum(line => line.ActualCbm);
            lpn.RecordedTemperature = lpn.PackageVariantLines.Select(line => line.RecordedTemperature).FirstOrDefault(value => value.HasValue);
            lpn.EvidenceImageUrl = string.Join(";", lpn.PackageVariantLines
                .Select(line => line.EvidenceImageUrl)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            maxDiff = lpn.PackageVariantLines.Max(line => line.DiffPercent);
            hasDiscrepancy = lpn.PackageVariantLines.Any(line => line.HasDiscrepancy);
        }
        else
        {
            var uploadedUrls = new List<string>();
            foreach (var file in request.EvidenceImages ?? new List<Microsoft.AspNetCore.Http.IFormFile>())
            {
                if (file.Length <= 0 || file.Length > 10 * 1024 * 1024)
                    return Failure($"File {file.FileName} must be non-empty and cannot exceed 10MB.");
                uploadedUrls.Add(await _fileService.UploadFileAsync(file));
            }

            var expectedCbm = InboundQcMeasurementCalculator.CalculateExpectedCbm(order.OrderDimension, order.Quantity);
            var actualCbm = InboundQcMeasurementCalculator.CalculateCbm(request.LengthCm, request.WidthCm, request.HeightCm, order.Quantity);
            var weightDiff = CalculateDiffPercent(order.OrderDimension.ExpectedWeightKg, request.ActualWeightKg);
            var cbmDiff = CalculateDiffPercent(expectedCbm, actualCbm);
            maxDiff = Math.Max(weightDiff, cbmDiff);
            hasDiscrepancy = maxDiff > DiscrepancyThresholdPercent;
            lpn.ActualWeightKg = request.ActualWeightKg;
            lpn.ActualCbm = actualCbm;
            lpn.LengthCm = request.LengthCm;
            lpn.WidthCm = request.WidthCm;
            lpn.HeightCm = request.HeightCm;
            lpn.RecordedTemperature = request.Temperature;
            if (uploadedUrls.Count > 0)
                lpn.EvidenceImageUrl = string.Join(";", uploadedUrls);
        }

        lpn.State = hasDiscrepancy ? LpnState.DISCREPANCY_HOLD : LpnState.RECEIVING;
        lpn.DiscrepancyReason = hasDiscrepancy
            ? $"Actual cargo differs from expected by {maxDiff:0.##}%. (Re-evaluated)"
            : null;
        lpn.UpdatedAt = now;

        var orderLpns = await _context.Lpns
            .Where(item => item.OrderId == order.OrderId && item.State != LpnState.DELETED)
            .ToListAsync(cancellationToken);
        var overallHasDiscrepancy = orderLpns.Any(item => item.State == LpnState.DISCREPANCY_HOLD);
        order.OrderDimension.ActualWeightKg = orderLpns.Sum(item => item.ActualWeightKg);
        order.OrderDimension.ActualCbm = orderLpns.Sum(item => item.ActualCbm);
        order.Status = overallHasDiscrepancy ? "DISCREPANCY_HOLD" : "RECEIVING";

        if (asn != null)
        {
            asn.Status = overallHasDiscrepancy ? "DISCREPANCY_HOLD" : "QC_PASSED";
        }

        receipt.ReferenceDocNo = overallHasDiscrepancy ? "DISCREPANCY_HOLD" : "PENDING_PUTAWAY";
        receipt.Reason = overallHasDiscrepancy ? "QC discrepancy hold (Re-evaluated)" : null;
        receipt.RecordedTemperature = lpn.RecordedTemperature;
        receipt.Note = overallHasDiscrepancy ? "QC discrepancy hold. (Re-evaluated)" : "QC passed and waiting putaway. (Re-evaluated)";

        await _context.SaveChangesAsync(cancellationToken);

        var pdfBytes = overallHasDiscrepancy
            ? await _mediator.Send(new GenerateDiscrepancyPdfQuery(receipt.ReceiptId), cancellationToken)
            : await _mediator.Send(new GenerateReceiptPdfQuery(receipt.ReceiptId), cancellationToken);

        var pdfFileName = overallHasDiscrepancy
            ? $"discrepancy-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf" 
            : $"grn-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf";
            
        var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);
        
        receipt.PdfUrl = pdfUrl;

        var existingDoc = await _context.TransportDocuments
            .FirstOrDefaultAsync(d => d.OrderId == order.OrderId && d.DocType == "DISCREPANCY_REPORT", cancellationToken);

        if (overallHasDiscrepancy)
        {
            if (existingDoc == null)
            {
                _context.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "DISCREPANCY_REPORT",
                    ImageUrl = pdfUrl,
                    UploadedBy = receipt.ReceiverId,
                    CreatedAt = now
                });
            }
            else
            {
                existingDoc.ImageUrl = pdfUrl;
                existingDoc.CreatedAt = now;
            }
        }
        else
        {
            if (existingDoc != null)
            {
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (hasDiscrepancy)
        {
            _logger.LogWarning(
                "Inbound QC re-evaluation: discrepancy still detected lpn={LpnCode} order={OrderId} maxDiff={MaxDiffPercent}",
                lpn.LpnCode,
                order.OrderId,
                maxDiff);
        }

        return new ReEvaluateInboundQcResponse
        {
            Success = true,
            Message = hasDiscrepancy
                ? "Re-evaluation completed. LPN remains in DISCREPANCY_HOLD."
                : "Re-evaluation passed successfully. LPN ready for putaway.",
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            DiffPercent = maxDiff,
            PdfUrl = pdfUrl,
            PackageLines = lpn.PackageVariantLines.Select(line => new ProcessInboundQcPackageLineResponse
            {
                LpnPackageVariantLineId = line.LpnPackageVariantLineId,
                OrderPackageVariantId = line.OrderPackageVariantId,
                PackageVariantName = line.VariantName,
                PackingType = line.PackingType,
                Quantity = line.Quantity,
                ActualWeightKg = line.ActualWeightKg,
                ActualCbm = line.ActualCbm,
                DiffPercent = line.DiffPercent,
                HasDiscrepancy = line.HasDiscrepancy
            }).ToList()
        };
    }

    private static ReEvaluateInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            throw new ArgumentOutOfRangeException(nameof(expected), "Expected measurement must be greater than 0.");

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }
}
