using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Payment;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Payment.Commands;

public class ReceivePaymentWebhookCommand : IRequest<ApiResponse<object>>
{
    public PaymentWebhookRequest Request { get; set; } = null!;
}

public class ReceivePaymentWebhookCommandHandler : IRequestHandler<ReceivePaymentWebhookCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public ReceivePaymentWebhookCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(ReceivePaymentWebhookCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Find TransportOrder by TrackingCode
        var order = await _context.TransportOrders
            .FirstOrDefaultAsync(o => o.TrackingCode == request.OrderCode, cancellationToken);
        if (order == null)
            throw new NotFoundException($"Order with code '{request.OrderCode}' was not found.");

        // 2. Find DeliveryEpod for this order
        var epod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId, cancellationToken);
        if (epod == null)
            throw new ValidationException($"No ePOD delivery record was found for order '{request.OrderCode}'.");

        if (request.Status.ToUpper() == "PAID")
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    // 3. Update ePOD payment details
                    epod.CodAmountPaid = request.Amount;
                    epod.PaymentStatus = "PAID";
                    epod.PaymentMethod = "QR";
                    epod.Note = $"{epod.Note} [QR Payment Verified. TxID: {request.TransactionId}]".Trim();

                    // 4. Sync Order Status (release the check-in gate)
                    var lpns = await _context.Lpns
                        .Where(l => l.OrderId == order.OrderId)
                        .ToListAsync(cancellationToken);

                    var allDelivered = lpns.All(l => l.State == LpnState.DELIVERED);
                    var allReturned = lpns.All(l => l.State == LpnState.RETURN_PENDING || l.State == LpnState.DELIVERY_RETURNED);

                    if (allDelivered)
                    {
                        order.Status = "DELIVERED";
                    }
                    else if (allReturned)
                    {
                        order.Status = "RETURNED";
                    }
                    else
                    {
                        order.Status = "PARTIALLY_DELIVERED";
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }

        return ApiResponse<object>.SuccessResponse(null, "Payment webhook received and processed successfully.");
    }
}
