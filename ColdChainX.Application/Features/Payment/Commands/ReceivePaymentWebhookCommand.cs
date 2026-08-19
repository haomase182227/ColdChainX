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

public class ReceivePaymentWebhookCommand : IRequest<ApiResponse<object>>
{
    public PaymentWebhookRequest Request { get; set; } = null!;

    public string? PayOsSignature { get; set; }

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

        if (!string.IsNullOrEmpty(command.PayOsSignature) && !string.IsNullOrEmpty(command.RawBody))
        {
            var isValid = _paymentGateway.VerifyWebhookSignature(command.RawBody, command.PayOsSignature);
            if (!isValid)
                throw new ForbiddenException("Invalid PayOS webhook signature. Request rejected.");
        }

        if (!string.Equals(request.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<object>.SuccessResponse(null, $"Webhook status '{request.Status}' acknowledged but no action taken.");

        var epod = await FindEpodByPayOsOrderCodeAsync(request.OrderCode, cancellationToken)
                   ?? await FindEpodByTrackingCodeAsync(request.OrderCode, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"No ePOD found for PayOS orderCode/trackingCode '{request.OrderCode}'.");

        if (epod.PaymentStatus == "PAID" || epod.PaymentStatus == "COD_SETTLED")
            return ApiResponse<object>.SuccessResponse(null, $"ePOD {epod.EpodId} already processed. Skipping.");

        var order = epod.Order;
        if (order == null)
            throw new ValidationException($"ePOD {epod.EpodId} is not linked to any order.");

        string? pdfUrl = null;
        if (order.OrderId != Guid.Empty)
        {
            try
            {
                pdfUrl = epod.HandoverPdfUrl ?? epod.PdfUrl;
            }
            catch
            {
            }
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var qrTx = new ColdChainX.Core.Entities.PaymentTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    TransactionCode = $"TX-PAYOS-{DateTime.UtcNow:yyyyMMddHHmmss}-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}",
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    TransactionType = "IN",
                    Amount = request.Amount,
                    PaymentMethod = "PAYOS",
                    ReferenceCode = request.TransactionId ?? request.OrderCode,
                    Status = "COMPLETED",
                    Note = $"Thanh toán QR PayOS thành công cho vận đơn {order.TrackingCode} (Chờ tài xế Verify).",
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };
                _context.PaymentTransactions.Add(qrTx);

                epod.PaymentStatus = "PAID";
                epod.CodAmountPaid = epod.CodAmount ?? request.Amount;
                epod.PaymentMethod = "QR";
                epod.PaymentConfirmedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });

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
        }

        return ApiResponse<object>.SuccessResponse(new
        {
            EpodId = epod.EpodId,
            OrderStatus = order.Status,
            EpodPdfUrl = pdfUrl
        }, "PayOS payment webhook processed successfully. ePOD finalized.");
    }


    private async Task<ColdChainX.Core.Entities.DeliveryEpod?> FindEpodByPayOsOrderCodeAsync(
        string orderCode, CancellationToken ct)
    {
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
