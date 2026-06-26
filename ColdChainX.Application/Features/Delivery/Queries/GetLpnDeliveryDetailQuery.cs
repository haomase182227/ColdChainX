using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetLpnDeliveryDetailQuery : IRequest<ApiResponse<LpnDeliveryStatusResponse>>
{
    public Guid TripId { get; set; }
    public Guid LpnId { get; set; }
}

public class GetLpnDeliveryDetailQueryHandler : IRequestHandler<GetLpnDeliveryDetailQuery, ApiResponse<LpnDeliveryStatusResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public GetLpnDeliveryDetailQueryHandler(IApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<ApiResponse<LpnDeliveryStatusResponse>> Handle(GetLpnDeliveryDetailQuery request, CancellationToken cancellationToken)
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

        // 3. Fetch delivery confirmation
        var confirmation = await _context.LpnDeliveryConfirmations
            .FirstOrDefaultAsync(c => c.LpnId == request.LpnId, cancellationToken);

        // Read bank configurations for VietQR
        var bankId = _configuration?["PaymentSettings:BankId"] ?? "vietinbank";
        var bankAccount = _configuration?["PaymentSettings:BankAccount"] ?? "1111111111";
        var bankAccountName = _configuration?["PaymentSettings:BankAccountName"] ?? "NGUYEN VAN A";

        var codAmount = confirmation != null ? confirmation.CodAmount : lpn.Order.CargoValue;

        string? vietQrUrl = null;
        if (lpn.State == LpnState.SHIPPING && codAmount > 0)
        {
            var memo = Uri.EscapeDataString($"ColdChainX LPN {lpn.LpnCode}");
            var accName = Uri.EscapeDataString(bankAccountName);
            vietQrUrl = $"https://img.vietqr.io/image/{bankId}-{bankAccount}-compact.png?amount={(int)codAmount}&addInfo={memo}&accountName={accName}";
        }

        // 4. Map to response
        var response = new LpnDeliveryStatusResponse
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            OutcomeType = confirmation?.OutcomeType,
            ReceiverName = confirmation?.ReceiverName,
            ReceiverPhone = confirmation?.ReceiverPhone,
            RejectReason = confirmation?.RejectReason,
            RejectNote = confirmation?.RejectNote,
            EvidenceImageUrl = confirmation?.EvidenceImageUrl,
            ConfirmedAt = confirmation?.ConfirmedAt,
            CheckinAt = confirmation?.CheckinAt,
            SignatureImageUrl = confirmation?.SignatureImageUrl,
            CodAmount = codAmount,
            CodPaymentMethod = confirmation?.CodPaymentMethod,
            CodReceiptImageUrl = confirmation?.CodReceiptImageUrl,
            NewSealNumber = confirmation?.NewSealNumber,
            VietQrUrl = vietQrUrl,
            IsCodVerified = confirmation?.IsCodVerified ?? false,
            CodVerifiedAt = confirmation?.CodVerifiedAt
        };

        return ApiResponse<LpnDeliveryStatusResponse>.SuccessResponse(response, "LPN delivery detail retrieved successfully.");
    }
}
