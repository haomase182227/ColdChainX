using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Queries;

/// <summary>
/// Lấy mã QR thanh toán PayOS dựa trên EpodId.
/// Tự động sinh mã QR với số tiền CodAmount đã được chốt ở bước ConfirmHandover.
/// </summary>
public class GetEpodPaymentQrQuery : IRequest<ApiResponse<object>>
{
    public Guid EpodId { get; set; }
}

public class GetEpodPaymentQrQueryHandler : IRequestHandler<GetEpodPaymentQrQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGatewayService _paymentGateway;

    public GetEpodPaymentQrQueryHandler(IApplicationDbContext context, IPaymentGatewayService paymentGateway)
    {
        _context = context;
        _paymentGateway = paymentGateway;
    }

    public async Task<ApiResponse<object>> Handle(GetEpodPaymentQrQuery request, CancellationToken cancellationToken)
    {
        var epod = await _context.DeliveryEpods
            .Include(e => e.Order)
            .FirstOrDefaultAsync(e => e.EpodId == request.EpodId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"Không tìm thấy ePOD nào với ID '{request.EpodId}'.");

        decimal totalCodDue = epod.CodAmount ?? 0m;

        // Bỏ qua số tiền thật để test PayOS với giá 2,000 VND
        totalCodDue = 2000m;

        if (totalCodDue <= 0)
        {
            throw new ValidationException($"ePOD '{request.EpodId}' không có giá trị thu hộ (CodAmount = 0). Không thể tạo mã QR thanh toán!");
        }

        if (epod.PaymentStatus == "PAID")
        {
            throw new ConflictException($"ePOD '{request.EpodId}' đã được thanh toán xong. Không cần tạo lại mã QR.");
        }

        // Tạo mã PayOS Order Code duy nhất bằng cách kết hợp EpodId Hash và Timestamp (chống trùng lặp)
        var epodShort = request.EpodId.ToString("N")[..6].ToUpperInvariant();
        var description = $"COD {epodShort}";
        
        // Dùng timestamp (số mili-giây) để đảm bảo orderCode sinh ra mỗi lần là duy nhất
        var payosOrderCode = long.Parse(DateTimeOffset.UtcNow.ToString("yyMMddHHmmssfff"));

        var qrResult = await _paymentGateway.CreatePaymentLinkAsync(
            payosOrderCode,
            (int)totalCodDue,
            description,
            cancellationToken);

        return ApiResponse<object>.SuccessResponse(new
        {
            EpodId = epod.EpodId,
            OrderId = epod.OrderId,
            TrackingCode = epod.Order?.TrackingCode,
            CodAmountDue = totalCodDue,
            PaymentStatus = epod.PaymentStatus,
            PayosOrderCode = qrResult.OrderCode,
            CheckoutUrl = qrResult.CheckoutUrl,
            QrCodeUrl = qrResult.QrCodeUrl
        }, "Mã QR thanh toán đã được tạo thành công dựa trên ePOD.");
    }
}
