using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

/// <summary>
/// Thanh toán theo Số Lượng Thực Nhận (Actual Received Quantity) tại Dock.
/// Cho phép truyền vào TripId + CustomerId + ActualReceivedQuantity: hệ thống tự động truy vấn vào Chuyến xe (Trip),
/// tìm đúng các Đơn hàng của Khách đó, đối chiếu Báo giá (Quotation) / COD gốc, và tự tính toán chính xác
/// số tiền phải trả theo tỷ lệ số lượng thực nhận (sau khi gạch bỏ phần lỗi OS&D), tạo bút toán IN hợp lệ!
/// </summary>
public class PayActualReceivedCodCommand : IRequest<ApiResponse<object>>
{
    public Guid? TripId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? EpodId { get; set; }

    public Guid UserId { get; set; }
    public string? PaymentMethod { get; set; } = "PAYOS_QR"; // "PAYOS_QR", "CASH", "BANK_TRANSFER"
    public int? ActualReceivedQuantity { get; set; } // Số lượng hàng thực nhận tại hiện trường Dock
    public decimal? ActualCodAmountPaid { get; set; } // Optional: nếu để trống, hệ thống TỰ ĐỘNG TÍNH từ Báo giá / COD
    public string? PaymentReferenceCode { get; set; }
    public string? Note { get; set; }
}

public class PayActualReceivedCodCommandHandler : IRequestHandler<PayActualReceivedCodCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public PayActualReceivedCodCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(PayActualReceivedCodCommand request, CancellationToken cancellationToken)
    {
        List<TransportOrder> orders = new();
        List<DeliveryEpod> epods = new();
        string targetReference = "";

        // 1. Nếu truyền vào TripId + CustomerId: Truy ngược vào Trip để tìm đúng các đơn hàng và Báo giá của Khách
        if (request.TripId.HasValue && request.TripId != Guid.Empty && request.CustomerId.HasValue && request.CustomerId != Guid.Empty)
        {
            orders = await _context.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.Quotations)
                .Include(o => o.DeliveryEpods)
                .Where(o => o.MasterTripId == request.TripId.Value && o.CustomerId == request.CustomerId.Value)
                .ToListAsync(cancellationToken);

            if (!orders.Any())
            {
                throw new NotFoundException($"Không tìm thấy đơn hàng nào thuộc chuyến xe '{request.TripId}' của Khách hàng '{request.CustomerId}'.");
            }

            epods = orders.SelectMany(o => o.DeliveryEpods).ToList();
            targetReference = $"TRIP-{request.TripId.Value.ToString()[..8].ToUpper()}-CUST-{request.CustomerId.Value.ToString()[..4].ToUpper()}";
        }
        // 2. Fallback: Nếu chỉ truyền EpodId (thao tác trực tiếp trên 1 tờ ePOD cụ thể)
        else if (request.EpodId.HasValue && request.EpodId != Guid.Empty)
        {
            var epod = await _context.DeliveryEpods
                .Include(e => e.Order)
                    .ThenInclude(o => o!.Quotations)
                .Include(e => e.Order)
                    .ThenInclude(o => o!.Customer)
                .FirstOrDefaultAsync(e => e.EpodId == request.EpodId.Value, cancellationToken);

            if (epod == null)
                throw new NotFoundException($"Không tìm thấy tờ biên nhận ePOD '{request.EpodId}'.");

            epods.Add(epod);
            if (epod.Order != null) orders.Add(epod.Order);
            targetReference = $"EPOD-{epod.EpodId.ToString()[..8].ToUpper()}";
        }
        else
        {
            return ApiResponse<object>.Failure("Vui lòng cung cấp cả (TripId + CustomerId) hoặc (EpodId) để thực hiện tính toán thu tiền COD thực nhận.");
        }

        // 3. Phân tích số lượng gốc và Báo giá (Quotation) / COD gốc của khách
        int totalOriginalQuantity = orders.Sum(o => o.Quantity);
        decimal originalQuotedAmount = orders.SelectMany(o => o.Quotations).Where(q => q.Status == "ACCEPTED" || q.Status == "APPROVED" || q.Status == "DRAFT").Sum(q => q.FinalAmount);

        // Nếu đơn hàng chưa gắn báo giá Quotation nào, lấy tổng COD ban đầu trên ePOD
        if (originalQuotedAmount <= 0)
        {
            originalQuotedAmount = epods.Sum(e => e.CodAmount ?? 0m);
            if (originalQuotedAmount <= 0 && request.ActualCodAmountPaid.HasValue)
            {
                originalQuotedAmount = request.ActualCodAmountPaid.Value;
            }
        }

        // 4. TÍNH TOÁN THỰC THU AUTOMATIC (Dựa trên Số Lượng Thực Nhận và Báo giá)
        decimal actualPayableAmount = 0m;
        decimal unitPrice = 0m;
        int resolvedActualQuantity = request.ActualReceivedQuantity ?? totalOriginalQuantity;

        if (request.ActualCodAmountPaid.HasValue && request.ActualCodAmountPaid.Value > 0)
        {
            // Trường hợp có nhập chỉ định số tiền thu trực tiếp
            actualPayableAmount = request.ActualCodAmountPaid.Value;
            unitPrice = resolvedActualQuantity > 0 ? Math.Round(actualPayableAmount / resolvedActualQuantity, 2) : 0m;
        }
        else if (totalOriginalQuantity > 0 && request.ActualReceivedQuantity.HasValue)
        {
            // Công thức: Tự động tính theo tỷ lệ từ Báo giá Gốc / Số lượng Gốc
            unitPrice = Math.Round(originalQuotedAmount / totalOriginalQuantity, 2);
            actualPayableAmount = Math.Round(request.ActualReceivedQuantity.Value * unitPrice, 2);
        }
        else
        {
            // Trả đủ nguyên trạng nếu không có sự cố từ chối hàng
            actualPayableAmount = originalQuotedAmount;
            unitPrice = totalOriginalQuantity > 0 ? Math.Round(originalQuotedAmount / totalOriginalQuantity, 2) : 0m;
        }

        // 5. Cập nhật các ePOD & Đơn hàng liên quan để các mã QR sau này quét ra số tiền mới
        string qtyText = request.ActualReceivedQuantity.HasValue ? $" (Số lượng thực nhận tại Dock: {request.ActualReceivedQuantity}/{totalOriginalQuantity} kiện. Đơn giá báo giá: {unitPrice:N0}đ)" : "";
        foreach (var epod in epods)
        {
            // Cập nhật hạ số tiền COD còn lại đúng bằng con số thực thu để tương thích 100% với QR PayOS
            if (epods.Count == 1)
            {
                epod.CodAmount = actualPayableAmount;
                epod.CodAmountPaid = actualPayableAmount;
            }
            else
            {
                // Phân bổ tỷ lệ thanh toán cho từng ePOD nếu gộp nhiều đơn trên chuyến
                decimal ratio = originalQuotedAmount > 0 ? (epod.CodAmount ?? 0m) / originalQuotedAmount : 1m / epods.Count;
                epod.CodAmount = Math.Round(actualPayableAmount * ratio, 2);
                epod.CodAmountPaid = epod.CodAmount;
            }

            epod.PaymentStatus = "PAID_ACTUAL_RECEIVED";
            epod.PaymentMethod = request.PaymentMethod ?? "PAYOS_QR";
            epod.PaymentConfirmedAt = DateTime.UtcNow;
            epod.Note = $"{epod.Note} | [Thanh toán Thực Thu Trip-Cust]: Chốt COD theo số lượng thực nhận{qtyText}. Tổng thu: {actualPayableAmount:N0} VNĐ qua {epod.PaymentMethod}.".Trim();
        }

        foreach (var order in orders)
        {
            order.Status = "DELIVERED_PAID_ACTUAL_RECEIVED";
        }

        // 6. Ghi chép ngay Bút toán IN vào Sổ cái Hệ thống PaymentTransactions
        string txCode = request.PaymentReferenceCode ?? $"PTX-IN-DOCK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var paymentTx = new PaymentTransaction
        {
            TransactionId = Guid.NewGuid(),
            TransactionCode = txCode,
            TransactionType = "IN", // Dòng tiền COD đi vào hệ thống
            OrderId = orders.FirstOrDefault()?.OrderId,
            Amount = actualPayableAmount,
            PaymentMethod = request.PaymentMethod ?? "PAYOS_QR",
            ReferenceCode = targetReference,
            Status = "COMPLETED",
            CreatedBy = request.UserId,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Note = $"Thu COD thực thu tại Dock theo Báo giá & Số lượng thực nhận ({resolvedActualQuantity} kiện). Tham chiếu: {targetReference}. Ghi chú: {request.Note}".Trim()
        };
        _context.PaymentTransactions.Add(paymentTx);

        await _context.SaveChangesAsync(cancellationToken);

        // 7. Trả về báo cáo tường minh cho Khách và Tài xế
        var customerName = orders.FirstOrDefault()?.Customer?.CompanyName ?? "Khách hàng";
        var result = new
        {
            Reference = targetReference,
            CustomerName = customerName,
            TotalOriginalQuantity = totalOriginalQuantity,
            ActualReceivedQuantity = resolvedActualQuantity,
            RejectedQuantity = Math.Max(0, totalOriginalQuantity - resolvedActualQuantity),
            OriginalQuotedAmount = originalQuotedAmount,
            CalculatedUnitPrice = unitPrice,
            ActualPayableAmount = actualPayableAmount,
            PaymentMethod = request.PaymentMethod,
            TransactionCode = paymentTx.TransactionCode,
            PaymentConfirmedAt = DateTime.UtcNow,
            Explanation = $"Hệ thống đã tự động đối chiếu Báo giá (Quotation) của {customerName}, tính ra đơn giá ({unitPrice:N0} VNĐ/kiện) và nhân với số lượng Thực Nhận ({resolvedActualQuantity} kiện) để chốt số tiền phải thanh toán là {actualPayableAmount:N0} VNĐ. Bút toán IN Sổ cái ({txCode}) đã hạch toán hoàn tất!"
        };

        return ApiResponse<object>.SuccessResponse(result, $"Tự động tính toán và ghi nhận thanh toán Thực Nhận cho Khách hàng trên Chuyến xe thành công.");
    }
}
