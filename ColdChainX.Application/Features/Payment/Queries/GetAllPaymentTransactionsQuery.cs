using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Payment.Queries;

public class GetAllPaymentTransactionsQuery : IRequest<ApiResponse<object>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Status { get; set; }
    public string? TransactionType { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class GetAllPaymentTransactionsQueryHandler : IRequestHandler<GetAllPaymentTransactionsQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetAllPaymentTransactionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(
        GetAllPaymentTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Lấy danh sách giao dịch chính thức trong bảng PaymentTransactions
        var dbTransactions = await _context.PaymentTransactions
            .Include(t => t.Order)
                .ThenInclude(o => o!.Customer)
            .Include(t => t.Customer)
            .AsNoTracking()
            .AsQueryable();

        if (status != null)
            transactionQuery = transactionQuery.Where(t => t.Status == status);
        if (transactionType != null)
            transactionQuery = transactionQuery.Where(t => t.TransactionType == transactionType);
        if (paymentMethod != null)
            transactionQuery = transactionQuery.Where(t => t.PaymentMethod == paymentMethod);
        if (request.FromDate.HasValue)
        {
            var start = AsDbDateTime(request.FromDate.Value);
            transactionQuery = transactionQuery.Where(t => t.CreatedAt >= start);
        }
        if (request.ToDate.HasValue)
        {
            var endExclusive = ToEndExclusive(AsDbDateTime(request.ToDate.Value));
            transactionQuery = transactionQuery.Where(t => t.CreatedAt < endExclusive);
        }

        var dbTransactions = await transactionQuery.ToListAsync(cancellationToken);
        var persistedOrderIds = await _context.PaymentTransactions
            .Where(t => t.OrderId.HasValue)
            .Select(t => t.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var results = dbTransactions.Select(t =>
        {
            var customer = t.Customer ?? t.Order?.Customer;
            return new PaymentTransactionListItem
            {
                TransactionId = t.TransactionId,
                TransactionCode = t.TransactionCode,
                OrderId = t.OrderId,
                TrackingCode = t.Order?.TrackingCode,
                CustomerId = t.CustomerId ?? t.Order?.CustomerId,
                CustomerName = customer?.CompanyName ?? BuildFallbackCustomerName(t.CustomerId ?? t.Order?.CustomerId),
                InvoiceId = t.InvoiceId,
                ClaimId = t.ClaimId,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                PaymentMethod = t.PaymentMethod,
                ReferenceCode = t.ReferenceCode,
                EvidenceImageUrl = t.EvidenceImageUrl,
                Status = t.Status,
                Note = t.Note,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt
            };
        }).ToList();

        var existingOrderIds = dbTransactions.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).ToHashSet();

        var epodQuery = _context.DeliveryEpods
            .Include(e => e.Order)
                .ThenInclude(o => o!.Customer)
            .Where(e => (e.PaymentStatus == "PAID" || e.CodAmountPaid > 0)
                        && e.OrderId != null
                        && !existingOrderIds.Contains(e.OrderId.Value))
            .AsNoTracking()
            .AsQueryable();

        // Synthesized ePOD rows always represent completed inbound transactions.
        if (status != null && status != "COMPLETED")
            epodQuery = epodQuery.Where(_ => false);
        if (transactionType != null && transactionType != "IN")
            epodQuery = epodQuery.Where(_ => false);
        if (paymentMethod != null)
            epodQuery = epodQuery.Where(e => (e.PaymentMethod ?? "PAYOS_QR") == paymentMethod);
        if (request.FromDate.HasValue)
        {
            var start = AsDbDateTime(request.FromDate.Value);
            epodQuery = epodQuery.Where(e => (e.PaymentConfirmedAt ?? e.CheckinTime) >= start);
        }
        if (request.ToDate.HasValue)
        {
            var endExclusive = ToEndExclusive(AsDbDateTime(request.ToDate.Value));
            epodQuery = epodQuery.Where(e => (e.PaymentConfirmedAt ?? e.CheckinTime) < endExclusive);
        }

        var paidEpods = await epodQuery.ToListAsync(cancellationToken);
        foreach (var epod in paidEpods)
        {
            var order = epod.Order!;
            var occurredAt = epod.PaymentConfirmedAt ?? epod.CheckinTime;
            results.Add(new PaymentTransactionListItem
            {
                TransactionId = epod.EpodId,
                TransactionCode = $"TX-EPOD-{epod.CheckinTime:yyyyMMdd}-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}",
                OrderId = order.OrderId,
                TrackingCode = order.TrackingCode,
                CustomerId = order.CustomerId,
                CustomerName = order.Customer?.CompanyName ?? BuildFallbackCustomerName(order.CustomerId),
                TransactionType = "IN",
                Amount = epod.CodAmountPaid ?? epod.CodAmount ?? 0m,
                PaymentMethod = epod.PaymentMethod ?? "PAYOS_QR",
                ReferenceCode = epod.PaymentEvidenceImageUrl != null ? "CASH_RECEIPT" : "PAYOS_DIRECT",
                EvidenceImageUrl = epod.PaymentEvidenceImageUrl ?? epod.SignImageUrl,
                Status = "COMPLETED",
                Note = epod.Note ?? "Thanh toán COD tại trạm giao hàng",
                CreatedAt = occurredAt,
                CompletedAt = occurredAt
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
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(sortedResults.Count / (double)pageSize),
            Transactions = paginatedResults
        }, "Lấy tất cả lịch sử giao dịch thanh toán thành công.");
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static DateTime ToEndExclusive(DateTime value)
        => value.TimeOfDay == TimeSpan.Zero ? value.Date.AddDays(1) : value.AddTicks(1);

    private static DateTime AsDbDateTime(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? BuildFallbackCustomerName(Guid? customerId)
        => customerId.HasValue ? $"Client {customerId.Value.ToString()[..8]}" : null;

    private sealed class PaymentTransactionListItem
    {
        public Guid TransactionId { get; init; }
        public string TransactionCode { get; init; } = string.Empty;
        public Guid? OrderId { get; init; }
        public string? TrackingCode { get; init; }
        public Guid? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public Guid? InvoiceId { get; init; }
        public Guid? ClaimId { get; init; }
        public string TransactionType { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string PaymentMethod { get; init; } = string.Empty;
        public string? ReferenceCode { get; init; }
        public string? EvidenceImageUrl { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? Note { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }
}
