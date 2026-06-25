using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetLpnDeliveryDetailQuery : IRequest<ApiResponse<LpnDeliveryStatusResponse>>
{
    public Guid TripId { get; set; }
    public Guid LpnId { get; set; }
}

public class GetLpnDeliveryDetailQueryHandler : IRequestHandler<GetLpnDeliveryDetailQuery, ApiResponse<LpnDeliveryStatusResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetLpnDeliveryDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<LpnDeliveryStatusResponse>> Handle(GetLpnDeliveryDetailQuery request, CancellationToken cancellationToken)
    {
        // 1. Fetch LPN and validate existence
        var lpn = await _context.Lpns
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
            throw new NotFoundException($"LPN with ID '{request.LpnId}' was not found.");

        // 2. Validate LPN belongs to specified trip
        if (lpn.TripId != request.TripId)
            throw new InvalidOperationException($"LPN '{lpn.LpnCode}' does not belong to trip '{request.TripId}'.");

        // 3. Fetch delivery confirmation
        var confirmation = await _context.LpnDeliveryConfirmations
            .FirstOrDefaultAsync(c => c.LpnId == request.LpnId, cancellationToken);

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
            ConfirmedAt = confirmation?.ConfirmedAt
        };

        return ApiResponse<LpnDeliveryStatusResponse>.SuccessResponse(response, "LPN delivery detail retrieved successfully.");
    }
}
