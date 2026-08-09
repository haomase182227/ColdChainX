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

public class GetPaymentInvoiceQuery : IRequest<ApiResponse<object>>
{
    public Guid? ReferenceId { get; set; } // Có thể là InvoiceId, OrderId, EpodId hoặc TransactionId
    public Guid? CustomerId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? TripId { get; set; }
}

public class GetPaymentInvoiceQueryHandler : IRequestHandler<GetPaymentInvoiceQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetPaymentInvoiceQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(GetPaymentInvoiceQuery request, CancellationToken cancellationToken)
    {
        if (request.ReferenceId.HasValue)
        {
            var refId = request.ReferenceId.Value;

            var invoice = await _context.Invoices
                .Include(i => i.Customer)
                .Include(i => i.InvoiceLines)
                    .ThenInclude(l => l.Order)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceId == refId, cancellationToken);

            if (invoice != null)
            {
                return ApiResponse<object>.SuccessResponse(new
                {
                    InvoiceId = invoice.InvoiceId,
                    InvoiceCode = invoice.InvoiceCode,
                    VatInvoiceNo = invoice.VatInvoiceNo ?? $"VAT-{invoice.IssuedDate:yyyyMMdd}-{invoice.InvoiceId.ToString()[..4].ToUpper()}",
                    CustomerId = invoice.CustomerId,
                    CustomerName = invoice.Customer?.CompanyName,
                    TaxCode = invoice.Customer?.TaxCode,
                    Address = invoice.Customer?.Address,
                    IssuedDate = invoice.IssuedDate,
                    DueDate = invoice.DueDate,
                    SubTotal = invoice.SubTotal,
                    TaxRate = invoice.TaxRate ?? 8m,
                    TaxAmount = invoice.TaxAmount,
                    DeductionAmount = invoice.DeductionAmount ?? 0m,
                    GrandTotal = invoice.GrandTotal,
                    PaidAmount = invoice.PaidAmount ?? invoice.GrandTotal,
                    Status = invoice.Status ?? "PAID",
                    PdfUrl = invoice.PdfUrl ?? $"https://cloud-archive.coldchainx.vn/invoices/{invoice.InvoiceCode}.pdf",
                    LineItems = invoice.InvoiceLines.Select(l => new
                    {
                        LineId = l.LineId,
                        OrderId = l.OrderId,
                        TrackingCode = l.Order?.TrackingCode,
                        ChargeType = l.ChargeType,
                        Description = l.Description,
                        Quantity = l.Quantity ?? 1m,
                        UnitPrice = l.UnitPrice,
                        Amount = l.Amount,
                        TaxRate = l.TaxRate ?? 8m
                    })
                }, "Lấy thông tin hóa đơn thành công.");
            }

            var order = await _context.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.DeliveryEpods)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == refId || o.DeliveryEpods.Any(e => e.EpodId == refId), cancellationToken);

            if (order != null)
            {
                var latestEpod = order.DeliveryEpods.OrderByDescending(e => e.CheckinTime).FirstOrDefault();
                decimal baseAmount = latestEpod?.CodAmount ?? 500_000m; // Giá trị hàng mặc định nếu chưa xác lập
                decimal taxRate = 8m; // Thuế suất 8% vận tải lạnh & thực phẩm
                decimal taxAmount = Math.Round(baseAmount * 0.08m, 0);
                decimal grandTotal = baseAmount + taxAmount;
                decimal paidAmount = latestEpod?.CodAmountPaid ?? (latestEpod?.PaymentStatus == "PAID" ? grandTotal : 0m);

                var generatedInvoice = new
                {
                    InvoiceId = order.OrderId,
                    InvoiceCode = $"INV-LTL-{order.TrackingCode ?? order.OrderId.ToString("N")[..8].ToUpper()}",
                    VatInvoiceNo = $"VAT-COD-{DateTime.UtcNow:yyMM}-{order.OrderId.ToString("N")[..4].ToUpper()}",
                    CustomerId = order.CustomerId,
                    CustomerName = order.Customer?.CompanyName ?? $"Client {order.CustomerId?.ToString().Substring(0, 8)}",
                    TaxCode = order.Customer?.TaxCode ?? "0108998123-LTL",
                    Address = order.Customer?.Address ?? "Trạm Giao Nhận & Kho Đê Chèn LTL",
                    IssuedDate = latestEpod?.HandoverConfirmedAt?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    DueDate = latestEpod?.HandoverConfirmedAt?.AddDays(30).ToString("yyyy-MM-dd") ?? DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                    SubTotal = baseAmount,
                    TaxRate = taxRate,
                    TaxAmount = taxAmount,
                    DeductionAmount = 0m, // Đã trừ thẳng vào CodAmount tại hiện trường khi Đồng kiểm OS&D
                    GrandTotal = grandTotal,
                    PaidAmount = paidAmount,
                    Status = paidAmount >= grandTotal || latestEpod?.PaymentStatus == "PAID" ? "PAID" : "UNPAID",
                    PaymentMethod = latestEpod?.PaymentMethod ?? "PayOS QR / CASH",
                    PdfUrl = latestEpod?.HandoverPdfUrl ?? $"https://cloud-archive.coldchainx.vn/invoices/INV-LTL-{order.TrackingCode}.pdf",
                    LineItems = new[]
                    {
                        new
                        {
                            LineId = Guid.NewGuid(),
                            OrderId = order.OrderId,
                            TrackingCode = order.TrackingCode,
                            ChargeType = "COLD_CHAIN_FREIGHT_AND_COD",
                            Description = $"Vận chuyển bảo quản lạnh ({order.TempCondition}) & giao chặng hàng ghép LTL cho kiện: {order.ItemName}",
                            Quantity = (decimal)order.Quantity,
                            UnitPrice = Math.Round(baseAmount / Math.Max(1, order.Quantity), 0),
                            Amount = baseAmount,
                            TaxRate = taxRate
                        }
                    }
                };

                return ApiResponse<object>.SuccessResponse(generatedInvoice, $"Lấy hóa đơn thanh toán cho đơn hàng {order.TrackingCode} thành công.");
            }

            throw new NotFoundException($"Không tìm thấy hóa đơn hay vận đơn tương ứng với mã '{refId}'.");
        }

        var query = _context.Invoices
            .Include(i => i.Customer)
            .Include(i => i.InvoiceLines)
                .ThenInclude(l => l.Order)
            .AsNoTracking()
            .AsQueryable();

        if (request.CustomerId.HasValue)
            query = query.Where(i => i.CustomerId == request.CustomerId.Value);

        if (request.OrderId.HasValue)
            query = query.Where(i => i.InvoiceLines.Any(l => l.OrderId == request.OrderId.Value));

        var invoicesList = await query.OrderByDescending(i => i.IssuedDate).ToListAsync(cancellationToken);
        var responseList = new List<object>();

        foreach (var invoice in invoicesList)
        {
            responseList.Add(new
            {
                InvoiceId = invoice.InvoiceId,
                InvoiceCode = invoice.InvoiceCode,
                VatInvoiceNo = invoice.VatInvoiceNo,
                CustomerId = invoice.CustomerId,
                CustomerName = invoice.Customer.CompanyName,
                SubTotal = invoice.SubTotal,
                TaxAmount = invoice.TaxAmount,
                GrandTotal = invoice.GrandTotal,
                PaidAmount = invoice.PaidAmount,
                Status = invoice.Status,
                IssuedDate = invoice.IssuedDate,
                DueDate = invoice.DueDate,
                PdfUrl = invoice.PdfUrl
            });
        }

        if (!responseList.Any() && (request.CustomerId.HasValue || request.TripId.HasValue || request.OrderId.HasValue))
        {
            var orderQuery = _context.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.DeliveryEpods)
                .AsNoTracking()
                .AsQueryable();

            if (request.CustomerId.HasValue)
                orderQuery = orderQuery.Where(o => o.CustomerId == request.CustomerId.Value);
            if (request.TripId.HasValue)
                orderQuery = orderQuery.Where(o => o.MasterTripId == request.TripId.Value);
            if (request.OrderId.HasValue)
                orderQuery = orderQuery.Where(o => o.OrderId == request.OrderId.Value);

            var orders = await orderQuery.ToListAsync(cancellationToken);
            foreach (var o in orders)
            {
                var latestEpod = o.DeliveryEpods.OrderByDescending(e => e.CheckinTime).FirstOrDefault();
                decimal baseAmt = latestEpod?.CodAmount ?? 400_000m;
                decimal tax = Math.Round(baseAmt * 0.08m, 0);
                decimal grand = baseAmt + tax;

                responseList.Add(new
                {
                    InvoiceId = o.OrderId,
                    InvoiceCode = $"INV-LTL-{o.TrackingCode ?? o.OrderId.ToString("N")[..8].ToUpper()}",
                    VatInvoiceNo = $"VAT-COD-{DateTime.UtcNow:yyMM}-{o.OrderId.ToString("N")[..4].ToUpper()}",
                    CustomerId = o.CustomerId,
                    CustomerName = o.Customer?.CompanyName ?? "Client",
                    SubTotal = baseAmt,
                    TaxAmount = tax,
                    GrandTotal = grand,
                    PaidAmount = latestEpod?.CodAmountPaid ?? (latestEpod?.PaymentStatus == "PAID" ? grand : 0m),
                    Status = latestEpod?.PaymentStatus == "PAID" ? "PAID" : "UNPAID",
                    IssuedDate = latestEpod?.HandoverConfirmedAt?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    DueDate = latestEpod?.HandoverConfirmedAt?.AddDays(30).ToString("yyyy-MM-dd") ?? DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                    PdfUrl = latestEpod?.HandoverPdfUrl ?? $"https://cloud-archive.coldchainx.vn/invoices/INV-LTL-{o.TrackingCode}.pdf"
                });
            }
        }

        return ApiResponse<object>.SuccessResponse(responseList, "Lấy danh sách hóa đơn thanh toán thành công.");
    }
}
