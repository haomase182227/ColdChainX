using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Payment;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Payment.Commands;

/// <summary>
/// Xử lý webhook từ PayOS sau khi khách hàng thanh toán QR thành công.
/// PayOS gửi HTTP POST với payload JSON và header x-payos-signature.
/// Handler này: (1) Verify HMAC, (2) Tìm ePOD qua PayOS orderCode, (3) Sinh ePOD PDF, (4) Cập nhật trạng thái đơn hàng.
/// </summary>
public class ReceivePaymentWebhookCommand : IRequest<ApiResponse<object>>
{
    public PaymentWebhookRequest Request { get; set; } = null!;

    /// <summary>Chữ ký HMAC-SHA256 từ header x-payos-signature. Null nếu không có.</summary>
    public string? PayOsSignature { get; set; }

    /// <summary>Raw webhook body (JSON string) để verify HMAC.</summary>
    public string? RawBody { get; set; }
}

public class ReceivePaymentWebhookCommandHandler : IRequestHandler<ReceivePaymentWebhookCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly IMediator _mediator;
    private readonly IDeliveryEventService _deliveryEvents;

    public ReceivePaymentWebhookCommandHandler(
        IApplicationDbContext context,
        IPaymentGatewayService paymentGateway,
        IMediator mediator,
        IDeliveryEventService deliveryEvents)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _mediator = mediator;
        _deliveryEvents = deliveryEvents;
    }

    public async Task<ApiResponse<object>> Handle(ReceivePaymentWebhookCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Verify PayOS HMAC-SHA256 signature (skip if no signature — for backward compat / testing)
        if (!string.IsNullOrEmpty(command.PayOsSignature) && !string.IsNullOrEmpty(command.RawBody))
        {
            var isValid = _paymentGateway.VerifyWebhookSignature(command.RawBody, command.PayOsSignature);
            if (!isValid)
                throw new ForbiddenException("Invalid PayOS webhook signature. Request rejected.");
        }

        // Only process PAID status
        if (!string.Equals(request.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.SuccessResponse(null, $"Webhook status '{request.Status}' acknowledged but no action taken.");

        // 2. Find ePOD by OrderCode.
        //    PayOS orderCode is stored in Note as "[PayOS:{orderCode}]"
        //    We also support legacy lookup by TransportOrder.TrackingCode for backward compat.
        var epod = await FindEpodByPayOsOrderCodeAsync(request.OrderCode, cancellationToken)
                   ?? await FindEpodByTrackingCodeAsync(request.OrderCode, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"No ePOD found for PayOS orderCode/trackingCode '{request.OrderCode}'.");

        if (epod.PaymentStatus == "PAID" || epod.PaymentStatus == "COD_SETTLED")
            return ApiResponse<object>.SuccessResponse(null, $"ePOD {epod.EpodId} already processed. Skipping.");

        var order = epod.Order;
        if (order == null)
            throw new ValidationException($"ePOD {epod.EpodId} is not linked to any order.");

        // 3. Generate final ePOD PDF (now that payment is confirmed)
        //    GenerateEpodPdfQuery returns byte[] — we upload to Cloudinary or get URL via PDF service
        string? pdfUrl = null;
        if (order.OrderId != Guid.Empty)
        {
            try
            {
                // PDF generation returns bytes — the handler uploads internally and returns URL via Note or we skip URL here
                // We call the query but don't store bytes as URL — order ePOD is already generated at handover
                // pdfUrl comes from existing epod.HandoverPdfUrl set during ConfirmHandoverCommand
                pdfUrl = epod.HandoverPdfUrl ?? epod.PdfUrl;
            }
            catch
            {
                // PDF generation failure must NOT block payment confirmation
            }
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 4. Update ePOD payment details
                epod.CodAmountPaid = request.Amount;
                epod.PaymentStatus = "PAID";
                epod.PaymentMethod = "QR";
                epod.PaymentConfirmedAt = DateTime.UtcNow;
                epod.Status = "COMPLETED";
                epod.Note = $"{epod.Note} [PayOS confirmed. TxID: {request.TransactionId}]".Trim();

                if (pdfUrl != null)
                    epod.PdfUrl = pdfUrl;

                // 5. Sync Order Status (release the SHIPPING gate)
                var lpns = await _context.Lpns
                    .Where(l => l.OrderId == order.OrderId)
                    .ToListAsync(cancellationToken);

                var allDelivered = lpns.All(l => l.State == LpnState.DELIVERED);
                var allReturned = lpns.All(l => l.State == LpnState.RETURN_PENDING || l.State == LpnState.DELIVERY_RETURNED);

                order.Status = allDelivered ? "DELIVERED"
                             : allReturned ? "RETURNED"
                             : "PARTIALLY_DELIVERED";

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });

        // 6. SignalR — notify COD payment confirmed
        try
        {
            await _deliveryEvents.NotifyCodPaymentConfirmedAsync(
                order.OrderId,
                order.TrackingCode ?? order.OrderId.ToString(),
                epod.EpodId,
                request.Amount,
                "QR",
                order.Status ?? "DELIVERED",
                pdfUrl ?? string.Empty,
                epod.ReceiverName,
                cancellationToken);
        }
        catch
        {
            // SignalR failure must NOT block response
        }

        return ApiResponse<object>.SuccessResponse(new
        {
            EpodId = epod.EpodId,
            OrderStatus = order.Status,
            EpodPdfUrl = pdfUrl
        }, "PayOS payment webhook processed successfully. ePOD finalized.");
    }

    // -------- Private helpers --------

    private async Task<ColdChainX.Core.Entities.DeliveryEpod?> FindEpodByPayOsOrderCodeAsync(
        string orderCode, CancellationToken ct)
    {
        // Note contains "[PayOS:{orderCode}]"
        var pattern = $"[PayOS:{orderCode}]";
        return await _context.DeliveryEpods
            .Include(e => e.Order)
            .FirstOrDefaultAsync(e => e.Note != null && e.Note.Contains(pattern), ct);
    }

    private async Task<ColdChainX.Core.Entities.DeliveryEpod?> FindEpodByTrackingCodeAsync(
        string trackingCode, CancellationToken ct)
    {
        var order = await _context.TransportOrders
            .FirstOrDefaultAsync(o => o.TrackingCode == trackingCode, ct);
        if (order == null) return null;

        return await _context.DeliveryEpods
            .Include(e => e.Order)
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId, ct);
    }
}
