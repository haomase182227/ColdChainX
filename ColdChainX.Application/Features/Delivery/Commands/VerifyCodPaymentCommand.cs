using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class VerifyCodPaymentCommand : IRequest<ApiResponse<LpnDeliveryStatusResponse>>
{
    public Guid TripId { get; set; }
    public Guid LpnId { get; set; }
    public Guid UserId { get; set; } // Set by Controller from JWT
}

public class VerifyCodPaymentCommandHandler : IRequestHandler<VerifyCodPaymentCommand, ApiResponse<LpnDeliveryStatusResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public VerifyCodPaymentCommandHandler(IApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<ApiResponse<LpnDeliveryStatusResponse>> Handle(VerifyCodPaymentCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch LPN and validate existence
        var lpn = await _context.Lpns
            .Include(l => l.Order)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
            throw new NotFoundException($"LPN with ID '{request.LpnId}' was not found.");

        // 2. Validate LPN belongs to specified trip
        if (lpn.TripId != request.TripId)
            throw new InvalidOperationException($"LPN '{lpn.LpnCode}' does not belong to trip '{request.TripId}'.");

        // 3. Fetch LPN delivery confirmation
        var confirmation = await _context.LpnDeliveryConfirmations
            .FirstOrDefaultAsync(c => c.LpnId == request.LpnId, cancellationToken);
        if (confirmation == null)
            throw new NotFoundException($"LPN delivery confirmation for '{lpn.LpnCode}' was not found.");

        if (confirmation.OutcomeType != "DELIVERED")
            throw new InvalidOperationException($"LPN '{lpn.LpnCode}' was not marked as DELIVERED (current outcome: {confirmation.OutcomeType}). COD verification is only allowed for delivered items.");

        // 4. Check if already verified
        if (confirmation.IsCodVerified)
            throw new ConflictException($"COD payment for LPN '{lpn.LpnCode}' has already been verified at {confirmation.CodVerifiedAt:yyyy-MM-ddTHH:mm:ssZ}.");

        // 5. Update confirmation and sync status
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                confirmation.IsCodVerified = true;
                confirmation.CodVerifiedAt = DateTime.UtcNow;
                confirmation.CodVerifiedByUserId = request.UserId;

                await _context.SaveChangesAsync(cancellationToken);

                // Sync Order status since COD is now verified
                await SyncOrderDeliveryStatusAsync(lpn.OrderId, cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // VietQR Generation in Response
                var bankId = _configuration?["PaymentSettings:BankId"] ?? "vietinbank";
                var bankAccount = _configuration?["PaymentSettings:BankAccount"] ?? "1111111111";
                var bankAccountName = _configuration?["PaymentSettings:BankAccountName"] ?? "NGUYEN VAN A";

                var codAmount = confirmation.CodAmount;
                string? vietQrUrl = null;
                if (lpn.State == LpnState.SHIPPING && codAmount > 0)
                {
                    var memo = Uri.EscapeDataString($"ColdChainX LPN {lpn.LpnCode}");
                    var accName = Uri.EscapeDataString(bankAccountName);
                    vietQrUrl = $"https://img.vietqr.io/image/{bankId}-{bankAccount}-compact.png?amount={(int)codAmount}&addInfo={memo}&accountName={accName}";
                }

                var response = new LpnDeliveryStatusResponse
                {
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode,
                    State = lpn.State.ToString(),
                    OutcomeType = confirmation.OutcomeType,
                    ReceiverName = confirmation.ReceiverName,
                    ReceiverPhone = confirmation.ReceiverPhone,
                    EvidenceImageUrl = confirmation.EvidenceImageUrl,
                    ConfirmedAt = confirmation.ConfirmedAt,
                    CheckinAt = confirmation.CheckinAt,
                    SignatureImageUrl = confirmation.SignatureImageUrl,
                    CodAmount = confirmation.CodAmount,
                    CodPaymentMethod = confirmation.CodPaymentMethod,
                    CodReceiptImageUrl = confirmation.CodReceiptImageUrl,
                    NewSealNumber = confirmation.NewSealNumber,
                    VietQrUrl = vietQrUrl,
                    IsCodVerified = confirmation.IsCodVerified,
                    CodVerifiedAt = confirmation.CodVerifiedAt
                };

                return ApiResponse<LpnDeliveryStatusResponse>.SuccessResponse(response, "COD payment verified successfully.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task SyncOrderDeliveryStatusAsync(Guid orderId, CancellationToken ct)
    {
        var lpns = await _context.Lpns.Where(l => l.OrderId == orderId).ToListAsync(ct);
        if (lpns.Count == 0) return;

        var anyShipping = lpns.Any(l => l.State == LpnState.SHIPPING);
        if (anyShipping) return;

        // Fetch confirmations to verify COD payments
        var lpnIds = lpns.Select(l => l.LpnId).ToList();
        var confirmations = await _context.LpnDeliveryConfirmations
            .Where(c => lpnIds.Contains(c.LpnId))
            .ToListAsync(ct);

        var hasUnverifiedCod = lpns.Any(l => l.State == LpnState.DELIVERED &&
            confirmations.Any(c => c.LpnId == l.LpnId && c.CodAmount > 0 && !c.IsCodVerified));

        if (hasUnverifiedCod)
        {
            return; // Gate: keep order status as SHIPPING until accountant approves COD payments
        }

        var order = await _context.TransportOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order == null) return;

        var allDelivered = lpns.All(l => l.State == LpnState.DELIVERED);
        var allReturned = lpns.All(l => l.State == LpnState.DELIVERY_RETURNED);

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
    }
}
