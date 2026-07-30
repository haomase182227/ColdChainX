using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Payment.Queries;

/// <summary>
/// Lấy lịch sử giao dịch thanh toán chi tiết của riêng một khách hàng (Customer),
/// kèm tổng hợp tài chính COD, bồi thường OS&D và dư nợ thanh toán.
/// </summary>
public class GetCustomerPaymentTransactionsQuery : IRequest<ApiResponse<object>>
{
    public Guid CustomerId { get; set; }
}

public class GetCustomerPaymentTransactionsQueryHandler : IRequestHandler<GetCustomerPaymentTransactionsQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetCustomerPaymentTransactionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(GetCustomerPaymentTransactionsQuery request, CancellationToken cancellationToken)
    {
        // Kiểm tra khách hàng hoặc đơn hàng tương ứng
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId, cancellationToken);

        var customerName = customer?.CompanyName ?? $"Client {request.CustomerId.ToString().Substring(0, 8)}";

        // 1. Lấy các giao dịch trong PaymentTransactions của khách này
        var dbTransactions = await _context.PaymentTransactions
            .Include(t => t.Order)
            .Where(t => t.CustomerId == request.CustomerId || (t.Order != null && t.Order.CustomerId == request.CustomerId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<object>();

        foreach (var t in dbTransactions)
        {
            results.Add(new
            {
                TransactionId = t.TransactionId,
                TransactionCode = t.TransactionCode,
                OrderId = t.OrderId,
                TrackingCode = t.Order?.TrackingCode,
                InvoiceId = t.InvoiceId,
                ClaimId = t.ClaimId,
                TransactionType = t.TransactionType, // "IN" (Khách trả COD/PayOS) hoặc "OUT" (Công ty trả bồi thường cho khách)
                Amount = t.Amount,
                PaymentMethod = t.PaymentMethod,
                ReferenceCode = t.ReferenceCode,
                EvidenceImageUrl = t.EvidenceImageUrl,
                Status = t.Status,
                Note = t.Note,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt ?? t.CreatedAt
            });
        }

        // 2. Hợp nhất thêm các ePOD đã thu tiền COD thuộc khách hàng này (nếu chưa có trong danh sách trên)
        var existingOrderIds = dbTransactions.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).ToHashSet();

        var paidEpods = await _context.DeliveryEpods
            .Include(e => e.Order)
            .Where(e => (e.PaymentStatus == "PAID" || e.CodAmountPaid > 0) && e.Order != null && e.Order.CustomerId == request.CustomerId && !existingOrderIds.Contains(e.Order.OrderId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var epod in paidEpods)
        {
            var order = epod.Order!;
            results.Add(new
            {
                TransactionId = epod.EpodId,
                TransactionCode = $"TX-COD-{epod.CheckinTime:yyMMdd}-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}",
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                InvoiceId = (Guid?)null,
                ClaimId = (Guid?)null,
                TransactionType = "IN",
                Amount = epod.CodAmountPaid ?? epod.CodAmount ?? 0m,
                PaymentMethod = epod.PaymentMethod ?? "PAYOS",
                ReferenceCode = epod.PaymentEvidenceImageUrl != null ? "CASH_RECEIPT" : "PAYOS_QR",
                EvidenceImageUrl = epod.PaymentEvidenceImageUrl ?? epod.SignImageUrl,
                Status = "COMPLETED",
                Note = epod.Note ?? "Quyết toán trọn gói COD sau khi trừ giảm trừ OS&D",
                CreatedAt = epod.PaymentConfirmedAt ?? epod.CheckinTime,
                CompletedAt = epod.PaymentConfirmedAt ?? epod.CheckinTime
            });
        }

        // 3. Sắp xếp danh sách mới nhất
        var sortedResults = results.OrderByDescending(r => ((dynamic)r).CreatedAt).ToList();

        decimal totalCodPaidByCustomer = sortedResults.Where(r => ((dynamic)r).TransactionType == "IN").Sum(r => (decimal)((dynamic)r).Amount);
        decimal totalCompensationPaidToCustomer = sortedResults.Where(r => ((dynamic)r).TransactionType == "OUT").Sum(r => (decimal)((dynamic)r).Amount);

        var financialSummary = new
        {
            CustomerId = request.CustomerId,
            CustomerName = customerName,
            TaxCode = customer?.TaxCode ?? "N/A",
            Address = customer?.Address ?? "N/A",
            TotalTransactionsCount = sortedResults.Count,
            TotalPaidCodAmount = totalCodPaidByCustomer,
            TotalCompensationAmount = totalCompensationPaidToCustomer,
            NetContainedBalance = totalCodPaidByCustomer - totalCompensationPaidToCustomer,
            Currency = "VNĐ",
            LastActiveDate = sortedResults.FirstOrDefault() != null ? ((dynamic)sortedResults.First()).CreatedAt : (DateTime?)null
        };

        return ApiResponse<object>.SuccessResponse(new
        {
            CustomerSummary = financialSummary,
            Transactions = sortedResults
        }, $"Lấy lịch sử giao dịch của khách hàng {customerName} thành công.");
    }
}
