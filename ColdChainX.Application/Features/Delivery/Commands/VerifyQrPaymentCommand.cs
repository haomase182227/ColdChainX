using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class VerifyQrPaymentCommand : IRequest<ApiResponse<object>>
{
    public Guid EpodId { get; set; }
    public Guid UserId { get; set; }

    public ColdChainX.Application.DTOs.Delivery.VerifyQrPaymentRequest Request { get; set; } = null!;
}

public class VerifyQrPaymentCommandHandler : IRequestHandler<VerifyQrPaymentCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;
    private readonly IPdfService _pdfService;

    public VerifyQrPaymentCommandHandler(IApplicationDbContext context, IFileService fileService, IPdfService pdfService)
    {
        _context = context;
        _fileService = fileService;
        _pdfService = pdfService;
    }

    public async Task<ApiResponse<object>> Handle(VerifyQrPaymentCommand request, CancellationToken cancellationToken)
    {
        List<TransportOrder> orders = new();
        List<DeliveryEpod> epods = new();
        string referenceTarget = "";

        string? paymentEvidenceUrl = null;
        if (request.Request.PaymentEvidenceFile != null)
        {
            paymentEvidenceUrl = await _fileService.UploadFileAsync(request.Request.PaymentEvidenceFile);
        }

        var epod = await _context.DeliveryEpods
            .Include(e => e.Order)
                .ThenInclude(o => o!.Customer)
            .FirstOrDefaultAsync(e => e.EpodId == request.EpodId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"Không tìm thấy tờ ePOD '{request.EpodId}'.");

        epods.Add(epod);
        if (epod.Order != null) orders.Add(epod.Order);
        referenceTarget = $"EPOD-{epod.EpodId.ToString()[..8].ToUpper()}";

        var orderIds = orders.Select(o => o.OrderId).ToList();
        string epodPrefix = request.EpodId.ToString().Substring(0, 8);
        var existingTransactions = await _context.PaymentTransactions
            .Where(t => (t.OrderId != null && orderIds.Contains(t.OrderId.Value)) || (!string.IsNullOrEmpty(epodPrefix) && t.ReferenceCode != null && t.ReferenceCode.Contains(epodPrefix)))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        bool isAlreadyConfirmedByWebhook = epods.Any(e => e.PaymentStatus == "PAID" || e.PaymentStatus == "PAID_ACTUAL_RECEIVED") 
                                           || existingTransactions.Any(t => t.Status == "COMPLETED" && t.TransactionType == "IN");

        string statusExplanation = "";
        string finalizedPaymentStatus = epods.FirstOrDefault()?.PaymentStatus ?? "UNPAID";

        if (!string.IsNullOrEmpty(paymentEvidenceUrl))
        {
            foreach (var e in epods)
            {
                e.PaymentEvidenceImageUrl = paymentEvidenceUrl;
                e.Note = $"{e.Note} | [Ảnh Bill Khách]: Đã đính kèm ảnh chụp màn hình chuyển khoản thành công.".Trim();

                if (!isAlreadyConfirmedByWebhook)
                {
                    e.PaymentStatus = "PAID_PROOF";
                    e.PaymentConfirmedAt = DateTime.UtcNow;
                    finalizedPaymentStatus = "PAID_PROOF";
                }
            }

            foreach (var order in orders)
            {
                var proofDoc = new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "PAYMENT_TRANSFER_SCREENSHOT",
                    ImageUrl = paymentEvidenceUrl,
                    CreatedAt = DateTime.UtcNow,
                    UploadedBy = request.UserId
                };
                _context.TransportDocuments.Add(proofDoc);

                if (!isAlreadyConfirmedByWebhook && order.Status == "AWAITING_QR")
                {
                    order.Status = "DELIVERED";
                }
            }
        }

        if (!existingTransactions.Any() && !string.IsNullOrEmpty(paymentEvidenceUrl))
        {
            decimal totalCod = epods.Sum(e => e.CodAmountPaid ?? e.CodAmount ?? 0m);
            var manualTx = new PaymentTransaction
            {
                TransactionId = Guid.NewGuid(),
                TransactionCode = $"PTX-IN-PROOF-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
                TransactionType = "IN",
                OrderId = orders.FirstOrDefault()?.OrderId,
                CustomerId = orders.FirstOrDefault()?.CustomerId,
                Amount = totalCod,
                PaymentMethod = "PAYOS_QR_PROOF",
                ReferenceCode = referenceTarget,
                EvidenceImageUrl = paymentEvidenceUrl,
                Status = isAlreadyConfirmedByWebhook ? "COMPLETED" : "PENDING_VERIFY",
                CreatedBy = request.UserId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = isAlreadyConfirmedByWebhook ? DateTime.UtcNow : null,
                Note = $"Tài xế upload ảnh chụp màn hình bill chuyển khoản thành công tại Dock."
            };
            _context.PaymentTransactions.Add(manualTx);
            existingTransactions.Add(manualTx);
        }
        else if (existingTransactions.Any() && !string.IsNullOrEmpty(paymentEvidenceUrl))
        {
            var tx = existingTransactions.First();
            tx.EvidenceImageUrl = paymentEvidenceUrl;
        }

        if (isAlreadyConfirmedByWebhook)
        {
            foreach (var e in epods)
            {
                if (e.PaymentStatus != "PAID")
                {
                    e.CodAmountPaid = e.CodAmount;
                    e.PaymentStatus = "PAID";
                    e.PaymentMethod = "QR";
                    e.PaymentConfirmedAt = DateTime.UtcNow;
                    e.Status = "COMPLETED";
                    finalizedPaymentStatus = "PAID";
                }
            }

            foreach (var order in orders)
            {
                if (order.Status == "AWAITING_QR" || order.Status == "PAID_PROOF")
                {
                    order.Status = "DELIVERED";
                }
            }
        }

        if (isAlreadyConfirmedByWebhook || !string.IsNullOrEmpty(paymentEvidenceUrl))
        {
            foreach (var order in orders)
            {
                var existingInvoice = await _context.Invoices
                    .Include(i => i.InvoiceLines)
                    .FirstOrDefaultAsync(i => i.InvoiceLines.Any(l => l.OrderId == order.OrderId), cancellationToken);

                if (existingInvoice == null)
                {
                    var relatedEpod = epods.FirstOrDefault(e => e.OrderId == order.OrderId);
                    decimal baseAmount = relatedEpod?.CodAmount ?? 500_000m;
                    decimal taxRate = 8m;
                    decimal taxAmount = Math.Round(baseAmount * 0.08m, 0);
                    decimal grandTotal = baseAmount + taxAmount;
                    decimal paidAmount = isAlreadyConfirmedByWebhook ? grandTotal : (relatedEpod?.CodAmountPaid ?? 0m);

                    var invoice = new ColdChainX.Core.Entities.Invoice
                    {
                        InvoiceId = Guid.NewGuid(),
                        InvoiceCode = $"INV-LTL-{order.TrackingCode ?? order.OrderId.ToString("N")[..8].ToUpper()}",
                        VatInvoiceNo = $"VAT-COD-{DateTime.UtcNow:yyMM}-{order.OrderId.ToString("N")[..4].ToUpper()}",
                        CustomerId = order.CustomerId ?? Guid.Empty,
                        SubTotal = baseAmount,
                        TaxRate = taxRate,
                        TaxAmount = taxAmount,
                        DeductionAmount = 0m,
                        GrandTotal = grandTotal,
                        PaidAmount = paidAmount,
                        Status = isAlreadyConfirmedByWebhook ? "PAID" : "UNPAID",
                        IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                        CreatedAt = DateTime.UtcNow,
                        Customer = order.Customer!
                    };

                    invoice.InvoiceLines.Add(new ColdChainX.Core.Entities.InvoiceLine
                    {
                        LineId = Guid.NewGuid(),
                        InvoiceId = invoice.InvoiceId,
                        OrderId = order.OrderId,
                        ChargeType = "COLD_CHAIN_FREIGHT_AND_COD",
                        Description = $"Vận chuyển bảo quản lạnh ({order.TempCondition}) & giao chặng hàng ghép LTL cho kiện: {order.ItemName}",
                        Quantity = (decimal)order.Quantity,
                        UnitPrice = Math.Round(baseAmount / Math.Max(1, order.Quantity), 0),
                        Amount = baseAmount,
                        TaxRate = taxRate,
                        Order = order
                    });

                    _context.Invoices.Add(invoice);

                    string htmlContent = ColdChainX.Application.Templates.InvoiceHtmlTemplate.GenerateHtml(invoice);
                    invoice.PdfUrl = await _pdfService.SaveInvoicePdfAsync(htmlContent, invoice.InvoiceCode);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (isAlreadyConfirmedByWebhook)
        {
            statusExplanation = "HỆ THỐNG ĐÃ NHẬN ĐƯỢC TIỀN TINH TINH! Webhook PayOS đã xác nhận giao dịch thành công. Ảnh màn hình bill được đính kèm thành hồ sơ đối soát chuẩn.";
        }
        else if (!string.IsNullOrEmpty(paymentEvidenceUrl))
        {
            statusExplanation = "Hệ thống đã lưu ảnh chụp màn hình chuyển khoản thành công từ khách hàng. Giao dịch được bảo lãnh sang 'PAID_VERIFIED_BY_PROOF', cho phép tài xế xuất phát! Kế toán sẽ đối soát tự động khi mạng Napas hoàn tất dồn tiền.";
        }
        else
        {
            statusExplanation = "Chưa phát hiện tín hiệu tiền về từ cổng PayOS và chưa có ảnh chụp màn hình chuyển khoản. Vui lòng bấm làm mới hoặc tải ảnh chụp bill để hoàn tất.";
        }

        var res = new
        {
            Reference = referenceTarget,
            CustomerName = orders.FirstOrDefault()?.Customer?.CompanyName ?? "Khách hàng",
            IsConfirmedBySystem = isAlreadyConfirmedByWebhook,
            CurrentPaymentStatus = finalizedPaymentStatus,
            PaymentEvidenceUrl = paymentEvidenceUrl ?? epods.FirstOrDefault()?.PaymentEvidenceImageUrl,
            LatestTransactionCode = existingTransactions.FirstOrDefault()?.TransactionCode,
            StatusSummary = statusExplanation,
            NextAction = isAlreadyConfirmedByWebhook || !string.IsNullOrEmpty(paymentEvidenceUrl)
                ? "Tài xế hoàn tất chốt đơn tại bãi! Sẵn sàng ấn nút rời điểm (Depart stop)."
                : "Chờ khách hoàn tất thanh toán hoặc nhờ khách gửi chụp màn hình chuyển khoản thành công."
        };

        return ApiResponse<object>.SuccessResponse(res, "Kiểm tra xác nhận thanh toán và đính kèm bill thành công.");
    }
}
