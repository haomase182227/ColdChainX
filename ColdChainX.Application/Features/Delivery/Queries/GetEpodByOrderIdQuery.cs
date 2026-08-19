using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetEpodByOrderIdQuery : IRequest<ApiResponse<EpodDto>>
{
    public Guid OrderId { get; set; }
}

public class GetEpodByOrderIdQueryHandler : IRequestHandler<GetEpodByOrderIdQuery, ApiResponse<EpodDto>>
{
    private readonly IApplicationDbContext _context;

    public GetEpodByOrderIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<EpodDto>> Handle(GetEpodByOrderIdQuery request, CancellationToken cancellationToken)
    {
        var epod = await _context.DeliveryEpods
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.OrderId == request.OrderId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"No ePOD found for OrderId '{request.OrderId}'.");

        var dto = new EpodDto
        {
            EpodId = epod.EpodId,
            OrderId = epod.OrderId,
            CheckinTime = epod.CheckinTime,
            SignedAt = epod.SignedAt,
            ReceiverName = epod.ReceiverName,
            ReceiverPhone = epod.ReceiverPhone,
            ReceiverConfirmed = epod.ReceiverConfirmed,
            SignImageUrl = epod.SignImageUrl,
            SignLatitude = epod.SignLatitude,
            SignLongitude = epod.SignLongitude,
            Status = epod.Status,
            CreatedAt = epod.CreatedAt,
            CodAmount = epod.CodAmount,
            CodAmountPaid = epod.CodAmountPaid,
            PaymentMethod = epod.PaymentMethod,
            PaymentStatus = epod.PaymentStatus,
            PaymentEvidenceImageUrl = epod.PaymentEvidenceImageUrl,
            HandoverConfirmedAt = epod.HandoverConfirmedAt,
            HandoverPdfUrl = epod.HandoverPdfUrl,
            PaymentConfirmedAt = epod.PaymentConfirmedAt
        };

        return ApiResponse<EpodDto>.SuccessResponse(dto);
    }
}
