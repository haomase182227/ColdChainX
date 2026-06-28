using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetTripDeliveryProgressQuery : IRequest<ApiResponse<TripDeliveryProgressResponse>>
{
    public Guid TripId { get; set; }
}

public class GetTripDeliveryProgressQueryHandler : IRequestHandler<GetTripDeliveryProgressQuery, ApiResponse<TripDeliveryProgressResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public GetTripDeliveryProgressQueryHandler(IApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<ApiResponse<TripDeliveryProgressResponse>> Handle(GetTripDeliveryProgressQuery request, CancellationToken cancellationToken)
    {
        // 1. Check if Trip exists
        var tripExists = await _context.MasterTrips
            .AnyAsync(t => t.TripId == request.TripId, cancellationToken);
        if (!tripExists)
            throw new NotFoundException($"Trip with ID '{request.TripId}' was not found.");

        // 2. Fetch all LPNs of the trip (including Order for CargoValue)
        var lpns = await _context.Lpns
            .Include(l => l.Order)
            .Where(l => l.TripId == request.TripId)
            .ToListAsync(cancellationToken);

        // 3. Fetch all delivery confirmations for this trip
        var confirmations = await _context.LpnDeliveryConfirmations
            .Where(c => c.TripId == request.TripId)
            .ToDictionaryAsync(c => c.LpnId, cancellationToken);

        // Read bank configurations for VietQR
        var bankId = _configuration?["PaymentSettings:BankId"] ?? "vietinbank";
        var bankAccount = _configuration?["PaymentSettings:BankAccount"] ?? "1111111111";
        var bankAccountName = _configuration?["PaymentSettings:BankAccountName"] ?? "NGUYEN VAN A";

        // 4. Map to LpnStatuses
        var lpnStatuses = new List<LpnDeliveryStatusResponse>();
        foreach (var lpn in lpns)
        {
            confirmations.TryGetValue(lpn.LpnId, out var conf);

            // Default COD amount is order's CargoValue if not confirmed yet
            var codAmount = conf != null ? conf.CodAmount : lpn.Order.CargoValue;

            string? vietQrUrl = null;
            if (lpn.State == LpnState.SHIPPING && codAmount > 0)
            {
                var memo = Uri.EscapeDataString($"ColdChainX LPN {lpn.LpnCode}");
                var accName = Uri.EscapeDataString(bankAccountName);
                vietQrUrl = $"https://img.vietqr.io/image/{bankId}-{bankAccount}-compact.png?amount={(int)codAmount}&addInfo={memo}&accountName={accName}";
            }

            lpnStatuses.Add(new LpnDeliveryStatusResponse
            {
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode,
                State = lpn.State.ToString(),
                OutcomeType = conf?.OutcomeType,
                ReceiverName = conf?.ReceiverName,
                ReceiverPhone = conf?.ReceiverPhone,
                RejectReason = conf?.RejectReason,
                RejectNote = conf?.RejectNote,
                EvidenceImageUrl = conf?.EvidenceImageUrl,
                ConfirmedAt = conf?.ConfirmedAt,
                CheckinAt = conf?.CheckinAt,
                SignatureImageUrl = conf?.SignatureImageUrl,
                CodAmount = codAmount,
                CodPaymentMethod = conf?.CodPaymentMethod,
                CodReceiptImageUrl = conf?.CodReceiptImageUrl,
                NewSealNumber = conf?.NewSealNumber,
                VietQrUrl = vietQrUrl,
                IsCodVerified = conf?.IsCodVerified ?? false,
                CodVerifiedAt = conf?.CodVerifiedAt,
                RecordedTemperature = conf?.RecordedTemperature ?? lpn.RecordedTemperature
            });
        }

        // 5. Aggregate counts
        var totalLpns = lpns.Count;
        var deliveredCount = lpns.Count(l => l.State == LpnState.DELIVERED);
        var rejectedCount = lpns.Count(l => l.State == LpnState.DELIVERY_RETURNED);
        var pendingCount = lpns.Count(l => l.State == LpnState.SHIPPING);
        var isComplete = totalLpns > 0 && pendingCount == 0;

        var progress = new TripDeliveryProgressResponse
        {
            TripId = request.TripId,
            TotalLpns = totalLpns,
            DeliveredCount = deliveredCount,
            RejectedCount = rejectedCount,
            PendingCount = pendingCount,
            IsComplete = isComplete,
            LpnStatuses = lpnStatuses
        };

        return ApiResponse<TripDeliveryProgressResponse>.SuccessResponse(progress, "Trip delivery progress retrieved successfully.");
    }
}
