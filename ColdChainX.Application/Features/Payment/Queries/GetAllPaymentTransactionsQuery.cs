using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Payment.Queries;

public class GetAllPaymentTransactionsQuery : IRequest<ApiResponse<object>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetAllPaymentTransactionsQueryHandler : IRequestHandler<GetAllPaymentTransactionsQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetAllPaymentTransactionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(GetAllPaymentTransactionsQuery request, CancellationToken cancellationToken)
    {
        var dbTransactions = await _context.PaymentTransactions
            .Include(t => t.Order)
                .ThenInclude(o => o!.Customer)
            .Include(t => t.Customer)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<object>();

        foreach (var t in dbTransactions)
        {
            var cust = t.Customer ?? t.Order?.Customer;
            results.Add(new
            {
                TransactionId = t.TransactionId,
                TransactionCode = t.TransactionCode,
                OrderId = t.OrderId,
                TrackingCode = t.Order?.TrackingCode,
                CustomerId = t.CustomerId ?? t.Order?.CustomerId,
                CustomerName = cust?.CompanyName ?? $"Client {(t.CustomerId ?? t.Order?.CustomerId)?.ToString().Substring(0, 8)}",
                InvoiceId = t.InvoiceId,
                ClaimId = t.ClaimId,
                TransactionType = t.TransactionType, // "IN" (COD/PayOS) hoặc "OUT" (Bồi thường Claim)
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
                .ThenInclude(o => o!.Customer)
            .Where(e => (e.PaymentStatus == "PAID" || e.CodAmountPaid > 0) && e.OrderId != null && !existingOrderIds.Contains(e.OrderId.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var epod in paidEpods)
        {
            var order = epod.Order!;
            results.Add(new
            {
                TransactionId = epod.EpodId,
                TransactionCode = $"TX-EPOD-{epod.CheckinTime:yyyyMMdd}-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}",
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.CompanyName ?? $"Client {order.CustomerId?.ToString().Substring(0, 8)}",
                InvoiceId = (Guid?)null,
                ClaimId = (Guid?)null,
                TransactionType = "IN",
                Amount = epod.CodAmountPaid ?? epod.CodAmount ?? 0m,
                PaymentMethod = epod.PaymentMethod ?? "PAYOS_QR",
                ReferenceCode = epod.PaymentEvidenceImageUrl != null ? "CASH_RECEIPT" : "PAYOS_DIRECT",
                EvidenceImageUrl = epod.PaymentEvidenceImageUrl ?? epod.SignImageUrl,
                Status = "COMPLETED",
                Note = epod.Note ?? "Thanh toán COD tại trạm giao hàng",
                CreatedAt = epod.PaymentConfirmedAt ?? epod.CheckinTime,
                CompletedAt = epod.PaymentConfirmedAt ?? epod.CheckinTime
            });
        }

        var sortedResults = results.OrderByDescending(r => ((dynamic)r).CreatedAt).ToList();

        decimal totalInFlow = sortedResults.Where(r => ((dynamic)r).TransactionType == "IN").Sum(r => (decimal)((dynamic)r).Amount);
        decimal totalOutFlow = sortedResults.Where(r => ((dynamic)r).TransactionType == "OUT").Sum(r => (decimal)((dynamic)r).Amount);

        var summary = new
        {
            TotalTransactionsCount = sortedResults.Count,
            TotalCodReceived = totalInFlow,
            TotalClaimOutflow = totalOutFlow,
            NetCashFlow = totalInFlow - totalOutFlow,
            Timestamp = DateTime.UtcNow
        };

        var paginatedResults = sortedResults.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToList();

        return ApiResponse<object>.SuccessResponse(new
        {
            Summary = summary,
            TotalCount = sortedResults.Count,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling((double)sortedResults.Count / request.PageSize),
            Transactions = paginatedResults
        }, "Lấy tất cả lịch sử giao dịch thanh toán thành công.");
    }
}
