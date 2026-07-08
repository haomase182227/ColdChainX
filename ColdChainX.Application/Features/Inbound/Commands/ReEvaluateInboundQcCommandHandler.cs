using ColdChainX.Application.Features.Discrepancy.Queries;
using ColdChainX.Application.Features.Inbound.Queries;
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
        var lpn = await _context.Lpns
            .Include(l => l.Receipt)
            .Include(l => l.Route) // for tracking
            .Include(l => l.Trip)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);

        if (lpn == null)
            return Failure("LPN not found.");

        if (lpn.State != LpnState.DISCREPANCY_HOLD)
            return Failure("LPN is not in DISCREPANCY_HOLD state. Cannot re-evaluate.");

        var order = await _context.TransportOrders
            .FirstOrDefaultAsync(o => o.OrderId == lpn.OrderId, cancellationToken);

        if (order == null)
            return Failure("Linked order not found.");

        var asn = await _context.InboundAsns
            .FirstOrDefaultAsync(a => a.OrderId == lpn.OrderId, cancellationToken);

        if (asn != null && asn.WarehouseId.HasValue && asn.WarehouseId.Value != request.WarehouseId)
            return Failure("ASN does not belong to current warehouse.");

        var receipt = lpn.Receipt;
        if (receipt == null)
            return Failure("Warehouse Receipt not found for this LPN.");

        string? evidenceImageUrl = lpn.EvidenceImageUrl;
        if (request.EvidenceImages != null && request.EvidenceImages.Count > 0)
        {
            var uploadedUrls = new System.Collections.Generic.List<string>();
            foreach (var file in request.EvidenceImages)
            {
                var url = await _fileService.UploadFileAsync(file);
                uploadedUrls.Add(url);
            }
            evidenceImageUrl = string.Join(";", uploadedUrls);
        }

        var now = DateTime.UtcNow;
        var actualCbm = CalculateCbm(request.LengthCm, request.WidthCm, request.HeightCm, order.Quantity);
        var weightDiff = CalculateDiffPercent(order.OrderDimension?.ExpectedWeightKg ?? 0m, request.ActualWeightKg);
        var cbmDiff = CalculateDiffPercent(order.OrderDimension?.ExpectedCbm ?? 0m, actualCbm);
        var maxDiff = Math.Max(weightDiff, cbmDiff);
        var hasDiscrepancy = maxDiff > DiscrepancyThresholdPercent;

        // Update LPN
        lpn.ActualWeightKg = request.ActualWeightKg;
        lpn.ActualCbm = actualCbm;
        lpn.RecordedTemperature = request.Temperature;
        lpn.State = hasDiscrepancy ? LpnState.DISCREPANCY_HOLD : LpnState.RECEIVING;
        lpn.DiscrepancyReason = hasDiscrepancy
            ? $"Actual cargo differs from expected by {maxDiff:0.##}% (weight {weightDiff:0.##}%, cbm {cbmDiff:0.##}%). (Re-evaluated)"
            : null;
        if (!string.IsNullOrEmpty(evidenceImageUrl))
        {
            lpn.EvidenceImageUrl = evidenceImageUrl;
        }
        lpn.UpdatedAt = now;

        // Update Order
        if (order.OrderDimension != null)
        {
            order.OrderDimension.ActualWeightKg = request.ActualWeightKg;
            order.OrderDimension.ActualCbm = actualCbm;
        }
        // User explicitly said: "Không cần update trạng thái của Order đâu"
        // order.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "RECEIVING";

        // Update ASN
        if (asn != null)
        {
            asn.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "QC_PASSED";
        }

        // Update Receipt
        receipt.ReferenceDocNo = hasDiscrepancy ? "DISCREPANCY_HOLD" : "PENDING_PUTAWAY";
        receipt.RecordedTemperature = request.Temperature;
        receipt.Note = hasDiscrepancy ? "QC discrepancy hold. (Re-evaluated)" : "QC passed and waiting putaway. (Re-evaluated)";

        await _context.SaveChangesAsync(cancellationToken);

        // Re-generate PDF
        var pdfBytes = hasDiscrepancy
            ? await _mediator.Send(new GenerateDiscrepancyPdfQuery(receipt.ReceiptId), cancellationToken)
            : await _mediator.Send(new GenerateReceiptPdfQuery(receipt.ReceiptId), cancellationToken);

        var pdfFileName = hasDiscrepancy 
            ? $"discrepancy-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf" 
            : $"grn-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf";
            
        var pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);
        
        receipt.PdfUrl = pdfUrl;

        // Create, update, or resolve TransportDocument for discrepancy report depending on hasDiscrepancy
        var existingDoc = await _context.TransportDocuments
            .FirstOrDefaultAsync(d => d.OrderId == order.OrderId && d.DocType == "DISCREPANCY_REPORT", cancellationToken);

        if (hasDiscrepancy)
        {
            if (existingDoc == null)
            {
                _context.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "DISCREPANCY_REPORT",
                    ImageUrl = pdfUrl,
                    Status = "PENDING",
                    UploadedBy = receipt.ReceiverId,
                    CreatedAt = now
                });
            }
            else
            {
                existingDoc.ImageUrl = pdfUrl;
                existingDoc.Status = "PENDING";
                existingDoc.CreatedAt = now;
            }
        }
        else
        {
            if (existingDoc != null)
            {
                existingDoc.Status = "APPROVED";
                existingDoc.VerifiedAt = now;
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
            PdfUrl = pdfUrl
        };
    }

    private static ReEvaluateInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * Math.Max(quantity, 1) / 1_000_000m, 4);

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return actual > 0 ? 100m : 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }
}
