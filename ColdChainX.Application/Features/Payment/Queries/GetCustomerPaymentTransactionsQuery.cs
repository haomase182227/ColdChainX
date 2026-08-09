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
        if (request.CustomerId == Guid.Empty)
            return ApiResponse<object>.Failure("CustomerId is required.", 400);

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId, cancellationToken);

        if (customer == null)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == request.CustomerId, cancellationToken);
            if (user != null)
            {
                customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email == user.Email, cancellationToken);
            }
        }

        if (customer == null)
            return ApiResponse<object>.Failure($"Customer entity with ID '{request.CustomerId}' was not found in database.", 404);

        var targetCustomerId = customer.CustomerId;
        var customerName = customer.CompanyName;

        var dbTransactions = await _context.PaymentTransactions
            .Include(t => t.Order)
            .Where(t => t.CustomerId == targetCustomerId || (t.Order != null && t.Order.CustomerId == targetCustomerId))
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

        var existingOrderIds = dbTransactions.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).ToHashSet();

        var paidEpods = await _context.DeliveryEpods
            .Include(e => e.Order)
            .Where(e => (e.PaymentStatus == "PAID" || e.CodAmountPaid > 0) && e.Order != null && e.Order.CustomerId == targetCustomerId && !existingOrderIds.Contains(e.Order.OrderId))
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

        var sortedResults = results.OrderByDescending(r => ((dynamic)r).CreatedAt).ToList();

        decimal totalCodPaidByCustomer = sortedResults.Where(r => ((dynamic)r).TransactionType == "IN").Sum(r => (decimal)((dynamic)r).Amount);
        decimal totalCompensationPaidToCustomer = sortedResults.Where(r => ((dynamic)r).TransactionType == "OUT").Sum(r => (decimal)((dynamic)r).Amount);

        var financialSummary = new
        {
            CustomerId = targetCustomerId,
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
