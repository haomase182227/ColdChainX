using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundQcCommandHandler : IRequestHandler<ProcessInboundQcCommand, ProcessInboundQcResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ProcessInboundQcCommandHandler> _logger;

    public ProcessInboundQcCommandHandler(IApplicationDbContext context, ILogger<ProcessInboundQcCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ProcessInboundQcResponse> Handle(ProcessInboundQcCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
        {
            return new ProcessInboundQcResponse { Success = false, Message = "LPN not found." };
        }

        if (lpn.State != LpnState.EXPECTED)
        {
            return new ProcessInboundQcResponse { Success = false, Message = $"Cannot process QC for LPN in state: {lpn.State}" };
        }

        // Update actual dimensions and weight
        lpn.ActualWeightKg = request.ActualWeightKg;
        lpn.LengthCm = request.LengthCm;
        lpn.WidthCm = request.WidthCm;
        lpn.HeightCm = request.HeightCm;
        
        // Calculate CBM = (L * W * H) / 1,000,000
        lpn.ActualCbm = (request.LengthCm * request.WidthCm * request.HeightCm) / 1000000m;

        // Apply 5% Rule on Weight (assuming expected weight is > 0 to avoid div zero)
        decimal diffPercent = 0;
        if (lpn.ExpectedWeightKg > 0)
        {
            diffPercent = Math.Abs(lpn.ActualWeightKg - lpn.ExpectedWeightKg) / lpn.ExpectedWeightKg * 100;
        }

        string resultMessage;
        string pdfUrl;

        if (diffPercent > 5m)
        {
            // Discrepancy case
            lpn.State = LpnState.DISCREPANCY_HOLD;
            lpn.DiscrepancyReason = $"Weight discrepancy > 5%. Expected: {lpn.ExpectedWeightKg}kg, Actual: {lpn.ActualWeightKg}kg (Diff: {diffPercent:F2}%)";
            
            // Mock Event & PDF
            _logger.LogWarning($"[SIGNALR_MOCK] Sending Notification to Sales: Discrepancy on LPN {lpn.LpnCode}");
            pdfUrl = $"https://coldchainx.mock/api/docs/discrepancy/{lpn.LpnId}.pdf";
            lpn.DiscrepancyPdfUrl = pdfUrl;
            
            resultMessage = "Discrepancy hold applied due to > 5% variance.";
        }
        else
        {
            // Normal case
            lpn.State = LpnState.RECEIVING;
            
            // Generate WarehouseReceipt if not exists
            var existingReceipt = await _context.WarehouseReceipts
                .FirstOrDefaultAsync(r => r.OrderId == lpn.OrderId && r.WarehouseId == request.WarehouseId, cancellationToken);
                
            if (existingReceipt == null)
            {
                existingReceipt = new WarehouseReceipt
                {
                    ReceiptId = Guid.NewGuid(),
                    ReceiptCode = $"REC-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                    OrderId = lpn.OrderId,
                    WarehouseId = request.WarehouseId,
                    ReceiptType = "INBOUND",
                    DelivererName = "System Generated",
                    ReceiverId = request.ReceiverId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.WarehouseReceipts.Add(existingReceipt);
            }

            // Create WarehouseReceiptItem
            var receiptItem = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ReceiptId = existingReceipt.ReceiptId,
                ItemName = $"LPN {lpn.LpnCode} Content",
                CountryOfOrigin = "Unknown",
                Unit = "Pallet/Box",
                ExpectedQty = 1,
                ActualQty = 1,
                ActualWeightKg = lpn.ActualWeightKg,
                LengthCm = lpn.LengthCm,
                WidthCm = lpn.WidthCm,
                HeightCm = lpn.HeightCm,
                BatchNumber = "BATCH-001"
            };
            _context.WarehouseReceiptItems.Add(receiptItem);

            lpn.ReceiptItemId = receiptItem.ItemId;

            // Mock PDF
            pdfUrl = $"https://coldchainx.mock/api/docs/grn/{lpn.LpnId}.pdf";
            lpn.GrnPdfUrl = pdfUrl;
            
            resultMessage = "QC Passed. LPN is now receiving. WarehouseReceipt verified.";
        }

        lpn.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return new ProcessInboundQcResponse 
        { 
            Success = true, 
            Message = resultMessage,
            PdfUrl = pdfUrl
        };
    }
}
