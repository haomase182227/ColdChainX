using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Features.Discrepancy.Commands;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services
{
    public class ContractAppendixService : IContractAppendixService
    {
        private const string Draft = "DRAFT";
        private const string Sent = "SENT";
        private const string Accepted = "ACCEPTED";
        private const string Rejected = "REJECTED";
        private const string Executed = "EXECUTED";

        private readonly ApplicationDbContext _db;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IMediator _mediator;

        public ContractAppendixService(
            ApplicationDbContext db,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IHubContext<NotificationHub> hubContext,
            IMediator mediator)
        {
            _db = db;
            _pdfService = pdfService;
            _environment = environment;
            _hubContext = hubContext;
            _mediator = mediator;
        }

        private const decimal MinChargeableWeightKg = 30m;

        public async Task<ApiResponse<string>> PreviewAppendixAsync(Guid orderId, decimal adjustedPrice, string reason)
        {
            var data = await LoadAppendixDataAsync(orderId);
            if (data == null)
                return ApiResponse<string>.Failure("Order, customer, or accepted contract/quotation was not found");

            if (data.Order.Status != "DISCREPANCY_HOLD")
                return ApiResponse<string>.Failure($"Order must be in DISCREPANCY_HOLD status. Current status: {data.Order.Status}");

            var template = await LoadAppendixTemplateAsync();
            var appendixNumber = await GenerateUniqueAppendixNumberAsync();
            var html = await RenderAppendixTemplateAsync(template, data, appendixNumber, adjustedPrice, reason);

            return ApiResponse<string>.SuccessResponse(html, "Appendix preview generated successfully");
        }

        public async Task<ApiResponse<ContractAppendixResponse>> GenerateAppendixAsync(Guid orderId, decimal? adjustedPrice, string reason, Guid salesUserId)
        {
            var data = await LoadAppendixDataAsync(orderId);
            if (data == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Order, customer, or accepted contract/quotation was not found");

            if (data.Order.Status != "DISCREPANCY_HOLD")
                return ApiResponse<ContractAppendixResponse>.Failure($"Order must be in DISCREPANCY_HOLD status. Current status: {data.Order.Status}");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                var existingAppendix = await _db.ContractAppendices
                    .FirstOrDefaultAsync(a => a.OrderId == orderId && (a.Status == Draft || a.Status == Sent));

                if (existingAppendix != null)
                    return ApiResponse<ContractAppendixResponse>.Failure($"An active contract appendix already exists for this order (Status: {existingAppendix.Status})");

                decimal resolvedAdjustedPrice = 0m;
                if (adjustedPrice.HasValue)
                {
                    resolvedAdjustedPrice = adjustedPrice.Value;
                }
                else
                {
                    var volumetricRate = await GetSystemConfigDecimalAsync("VolumetricConversionRate", 250m);
                    var actualCbm = data.Order.ActualCbm ?? data.Order.ExpectedCbm;
                    var volumetricWeight = Math.Round(actualCbm * volumetricRate, 2);
                    var chargeableWeight = Math.Max(Math.Max(data.Order.ActualWeightKg, volumetricWeight), MinChargeableWeightKg);

                    var tier = await _db.WeightTiers
                        .AsNoTracking()
                        .Where(t => t.RouteId == data.Order.RouteId.Value
                                    && chargeableWeight >= t.MinWeightKg
                                    && (!t.MaxWeightKg.HasValue || chargeableWeight <= t.MaxWeightKg.Value))
                        .OrderByDescending(t => t.MinWeightKg)
                        .FirstOrDefaultAsync();

                    if (tier == null)
                        return ApiResponse<ContractAppendixResponse>.Failure($"Weight tier is missing for route and chargeable weight {chargeableWeight} kg");

                    var newBaseFreight = Math.Round(chargeableWeight * tier.PricePerKg, 0);
                    var originalBaseFreight = data.Quotation.BaseFreight;
                    resolvedAdjustedPrice = newBaseFreight - originalBaseFreight;
                }

                var appendixNumber = await GenerateUniqueAppendixNumberAsync();
                var template = await LoadAppendixTemplateAsync();
                var htmlContent = await RenderAppendixTemplateAsync(template, data, appendixNumber, resolvedAdjustedPrice, reason);

                var appendix = new ContractAppendix
                {
                    AppendixId = Guid.NewGuid(),
                    ContractId = data.Contract?.ContractId,
                    OrderId = orderId,
                    AppendixNumber = appendixNumber,
                    AdjustedPrice = resolvedAdjustedPrice,
                    Reason = reason,
                    Status = Draft,
                    DraftHtmlContent = htmlContent,
                    CreatedAt = DbNow()
                };

                _db.ContractAppendices.Add(appendix);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix draft generated");
            });
        }

        public async Task<ApiResponse<ContractAppendixResponse>> UpdateAppendixDraftAsync(Guid appendixId, string editedHtmlContent, Guid salesUserId)
        {
            if (string.IsNullOrWhiteSpace(editedHtmlContent) || !editedHtmlContent.TrimStart().StartsWith("<"))
                return ApiResponse<ContractAppendixResponse>.Failure("EditedHtmlContent must be valid HTML");

            var appendix = await _db.ContractAppendices.FirstOrDefaultAsync(a => a.AppendixId == appendixId);
            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            if (appendix.Status != Draft)
                return ApiResponse<ContractAppendixResponse>.Failure("Only DRAFT contract appendices can be updated");

            appendix.DraftHtmlContent = editedHtmlContent;
            await _db.SaveChangesAsync();

            return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix draft updated");
        }

        public async Task<ApiResponse<ContractAppendixResponse>> SendAppendixAsync(Guid appendixId, Guid salesUserId)
        {
            var appendix = await _db.ContractAppendices
                .Include(a => a.Order)
                .FirstOrDefaultAsync(a => a.AppendixId == appendixId);

            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            if (appendix.Status != Draft)
                return ApiResponse<ContractAppendixResponse>.Failure("Only DRAFT contract appendices can be sent");

            var html = appendix.DraftHtmlContent;
            if (string.IsNullOrWhiteSpace(html))
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix has no HTML draft content");

            var pdfUrl = await _pdfService.SaveContractAppendixPdfAsync(html, appendix.AppendixNumber);
            appendix.PdfUrl = pdfUrl;
            appendix.Status = Sent;
            appendix.SentAt = DbNow();

            await EnsureNotificationTemplateAsync("NOTI_APPENDIX_PENDING_SIGNATURE");
            var customerUserId = await ResolveCustomerUserIdAsync(appendix.Order.CustomerId);
            await AddNotificationAsync(
                customerUserId,
                salesUserId,
                "NOTI_APPENDIX_PENDING_SIGNATURE",
                appendix.OrderId,
                new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode, File_URL = pdfUrl });

            // Notify Sales as well
            await EnsureNotificationTemplateAsync("NOTI_APPENDIX_SENT");
            await AddNotificationAsync(
                salesUserId,
                salesUserId,
                "NOTI_APPENDIX_SENT",
                appendix.OrderId,
                new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode });

            await _db.SaveChangesAsync();

            await _hubContext.Clients.User(appendix.Order.CustomerId.ToString()!).SendAsync("AppendixPendingSignature", new
            {
                appendix.AppendixId,
                appendix.AppendixNumber,
                appendix.PdfUrl,
                appendix.Status,
                appendix.OrderId
            });

            await _hubContext.Clients.Group("Group_Sales").SendAsync("AppendixSent", new
            {
                appendix.AppendixId,
                appendix.AppendixNumber,
                appendix.PdfUrl,
                appendix.Status,
                appendix.OrderId
            });

            return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix sent to customer");
        }

        public async Task<ApiResponse<ContractAppendixResponse>> AcceptAppendixAsync(Guid appendixId, Guid customerId)
        {
            var appendix = await _db.ContractAppendices
                .Include(a => a.Order)
                .FirstOrDefaultAsync(a => a.AppendixId == appendixId);

            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            if (appendix.Status != Sent)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix is not in SENT state");

            if (appendix.Order.CustomerId != customerId)
                return ApiResponse<ContractAppendixResponse>.Failure("CustomerId does not match the order customer");

            appendix.Status = Accepted;
            appendix.ResolvedAt = DbNow();

            var salesUserId = await ResolveSalesUserIdAsync();
            var customerUserId = await ResolveCustomerUserIdAsync(customerId);
            await EnsureNotificationTemplateAsync("NOTI_APPENDIX_ACCEPTED");
            await AddNotificationAsync(
                salesUserId,
                customerUserId,
                "NOTI_APPENDIX_ACCEPTED",
                appendix.OrderId,
                new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode });

            await _db.SaveChangesAsync();

            await _hubContext.Clients.Group("Group_Sales").SendAsync("AppendixAccepted", new
            {
                appendix.AppendixId,
                appendix.AppendixNumber,
                appendix.OrderId
            });

            return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix accepted by customer");
        }

        public async Task<ApiResponse<ContractAppendixResponse>> RejectAppendixAsync(Guid appendixId, Guid customerId)
        {
            var appendix = await _db.ContractAppendices
                .Include(a => a.Order)
                .FirstOrDefaultAsync(a => a.AppendixId == appendixId);

            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            if (appendix.Status != Sent)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix is not in SENT state");

            if (appendix.Order.CustomerId != customerId)
                return ApiResponse<ContractAppendixResponse>.Failure("CustomerId does not match the order customer");

            var lpn = await _db.Lpns.FirstOrDefaultAsync(l => l.OrderId == appendix.OrderId);
            if (lpn == null)
                return ApiResponse<ContractAppendixResponse>.Failure("No LPN found for this order");

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                // 1. Resolve Discrepancy (Accept = false, charge 200,000 VND handling fee)
                const decimal PenaltyAmount = 200000m;
                var resolveCmd = new ResolveDiscrepancyCommand
                {
                    LpnId = lpn.LpnId,
                    Accept = false,
                    PenaltyAmount = PenaltyAmount,
                    PenaltyReason = "Discrepancy appendix rejected by customer"
                };
                var resolveRes = await _mediator.Send(resolveCmd);
                if (!resolveRes.Success)
                {
                    await transaction.RollbackAsync();
                    return ApiResponse<ContractAppendixResponse>.Failure($"Failed to resolve discrepancy: {resolveRes.Message}");
                }

                // 2. Create Inbound Return Slip
                var slipCode = await GenerateUniqueSlipCodeAsync();
                var returnSlip = new InboundReturnSlip
                {
                    ReturnSlipId = Guid.NewGuid(),
                    OrderId = appendix.OrderId,
                    LpnId = lpn.LpnId,
                    SlipCode = slipCode,
                    ReturnedWeightKg = lpn.ActualWeightKg,
                    ReturnedCbm = lpn.ActualCbm,
                    ReturnedQty = lpn.Quantity,
                    Reason = "Discrepancy appendix rejected by customer",
                    CreatedAt = DbNow()
                };
                _db.InboundReturnSlips.Add(returnSlip);

                // Save slip code changes so PDF generator has data
                await _db.SaveChangesAsync();

                // Generate Inbound Return Slip PDF
                try
                {
                    returnSlip.PdfUrl = await GenerateReturnSlipPdfAsync(appendix.Order, lpn, returnSlip, PenaltyAmount);
                }
                catch (Exception ex)
                {
                    // Proceed
                }

                // 3. Mark appendix as EXECUTED (directly executed since it is auto-resolved)
                appendix.Status = Executed;
                appendix.ResolvedAt = DbNow();

                var salesUserId = await ResolveSalesUserIdAsync();
                var customerUserId = await ResolveCustomerUserIdAsync(customerId);
                await EnsureNotificationTemplateAsync("NOTI_APPENDIX_REJECTED");
                await AddNotificationAsync(
                    salesUserId,
                    customerUserId,
                    "NOTI_APPENDIX_REJECTED",
                    appendix.OrderId,
                    new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode });

                // Also notify execute to Sales
                await EnsureNotificationTemplateAsync("NOTI_APPENDIX_EXECUTED");
                await AddNotificationAsync(
                    salesUserId,
                    salesUserId,
                    "NOTI_APPENDIX_EXECUTED",
                    appendix.OrderId,
                    new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Retrieve the created PenaltyBill
                var penaltyBill = await _db.PenaltyBills.FirstOrDefaultAsync(pb => pb.OrderId == appendix.OrderId);

                var resp = ToResponse(appendix);
                if (penaltyBill != null)
                {
                    resp.PenaltyBill = new PenaltyBillResponse
                    {
                        PenaltyBillId = penaltyBill.PenaltyBillId,
                        BillCode = penaltyBill.BillCode,
                        LpnId = penaltyBill.LpnId,
                        OrderId = penaltyBill.OrderId,
                        HandlingFee = penaltyBill.HandlingFee,
                        StorageFee = penaltyBill.StorageFee,
                        TotalAmount = penaltyBill.TotalAmount,
                        Reason = penaltyBill.Reason,
                        IsPaid = penaltyBill.IsPaid,
                        CreatedAt = penaltyBill.CreatedAt,
                        PaidAt = penaltyBill.PaidAt
                    };
                }
                resp.ReturnSlip = new InboundReturnSlipResponse
                {
                    ReturnSlipId = returnSlip.ReturnSlipId,
                    OrderId = returnSlip.OrderId,
                    LpnId = returnSlip.LpnId,
                    SlipCode = returnSlip.SlipCode,
                    ReturnedWeightKg = returnSlip.ReturnedWeightKg,
                    ReturnedCbm = returnSlip.ReturnedCbm,
                    ReturnedQty = returnSlip.ReturnedQty,
                    Reason = returnSlip.Reason,
                    PdfUrl = returnSlip.PdfUrl,
                    CreatedAt = returnSlip.CreatedAt
                };

                await _hubContext.Clients.Group("Group_Sales").SendAsync("AppendixRejected", new
                {
                    appendix.AppendixId,
                    appendix.AppendixNumber,
                    appendix.OrderId
                });

                await _hubContext.Clients.Group("Group_Sales").SendAsync("AppendixExecuted", new
                {
                    appendix.AppendixId,
                    appendix.AppendixNumber,
                    appendix.Status,
                    appendix.OrderId
                });

                return ApiResponse<ContractAppendixResponse>.SuccessResponse(resp, "Contract appendix rejected by customer and discrepancy executed immediately");
            });
        }

        public async Task<ApiResponse<ContractAppendixResponse>> ExecuteAppendixResolutionAsync(Guid appendixId, Guid salesUserId)
        {
            var appendix = await _db.ContractAppendices
                .Include(a => a.Order)
                    .ThenInclude(o => o.Customer)
                .FirstOrDefaultAsync(a => a.AppendixId == appendixId);

            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            if (appendix.Status != Accepted && appendix.Status != Rejected)
                return ApiResponse<ContractAppendixResponse>.Failure($"Appendix resolution can only be executed if status is ACCEPTED or REJECTED. Current: {appendix.Status}");

            var lpn = await _db.Lpns.FirstOrDefaultAsync(l => l.OrderId == appendix.OrderId);
            if (lpn == null)
                return ApiResponse<ContractAppendixResponse>.Failure("No LPN found for this order");

            var wasRejected = appendix.Status == Rejected;

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _db.Database.BeginTransactionAsync();

                if (appendix.Status == Accepted)
                {
                    // 1. Resolve Discrepancy (Accept = true)
                    var resolveCmd = new ResolveDiscrepancyCommand
                    {
                        LpnId = lpn.LpnId,
                        Accept = true
                    };
                    var resolveRes = await _mediator.Send(resolveCmd);
                    if (!resolveRes.Success)
                    {
                        await transaction.RollbackAsync();
                        return ApiResponse<ContractAppendixResponse>.Failure($"Failed to resolve discrepancy: {resolveRes.Message}");
                    }

                    // 2. Create Adjustment Invoice
                    var invoiceCode = await GenerateUniqueInvoiceCodeAsync();
                    var invoice = new Invoice
                    {
                        InvoiceId = Guid.NewGuid(),
                        InvoiceCode = invoiceCode,
                        CustomerId = appendix.Order.CustomerId.GetValueOrDefault(),
                        SubTotal = appendix.AdjustedPrice,
                        TaxRate = 0.08m,
                        TaxAmount = Math.Round(appendix.AdjustedPrice * 0.08m, 0),
                        GrandTotal = appendix.AdjustedPrice + Math.Round(appendix.AdjustedPrice * 0.08m, 0),
                        PaidAmount = 0,
                        IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
                        Status = "UNPAID",
                        CreatedAt = DbNow()
                    };
                    _db.Invoices.Add(invoice);

                    var invoiceLine = new InvoiceLine
                    {
                        LineId = Guid.NewGuid(),
                        InvoiceId = invoice.InvoiceId,
                        OrderId = appendix.OrderId,
                        ChargeType = "INBOUND_MEASUREMENT_ADJUSTMENT",
                        Description = $"Adjustment fee based on contract appendix {appendix.AppendixNumber} (weight/CBM discrepancy)",
                        Quantity = 1,
                        UnitPrice = appendix.AdjustedPrice,
                        Amount = appendix.AdjustedPrice,
                        TaxRate = 0.08m
                    };
                    _db.InvoiceLines.Add(invoiceLine);

                    // Generate Invoice PDF
                    try
                    {
                        invoice.PdfUrl = await GenerateInvoicePdfAsync(appendix.Order, invoice, invoiceLine);
                    }
                    catch (Exception ex)
                    {
                        // Log warning but proceed
                    }
                }
                else if (appendix.Status == Rejected)
                {
                    // 1. Resolve Discrepancy (Accept = false, charge 200,000 VND handling fee)
                    const decimal PenaltyAmount = 200000m;
                    var resolveCmd = new ResolveDiscrepancyCommand
                    {
                        LpnId = lpn.LpnId,
                        Accept = false,
                        PenaltyAmount = PenaltyAmount,
                        PenaltyReason = "Discrepancy appendix rejected by customer"
                    };
                    var resolveRes = await _mediator.Send(resolveCmd);
                    if (!resolveRes.Success)
                    {
                        await transaction.RollbackAsync();
                        return ApiResponse<ContractAppendixResponse>.Failure($"Failed to resolve discrepancy: {resolveRes.Message}");
                    }

                    // 2. Create Inbound Return Slip
                    var slipCode = await GenerateUniqueSlipCodeAsync();
                    var returnSlip = new InboundReturnSlip
                    {
                        ReturnSlipId = Guid.NewGuid(),
                        OrderId = appendix.OrderId,
                        LpnId = lpn.LpnId,
                        SlipCode = slipCode,
                        ReturnedWeightKg = lpn.ActualWeightKg,
                        ReturnedCbm = lpn.ActualCbm,
                        ReturnedQty = lpn.Quantity,
                        Reason = "Discrepancy appendix rejected by customer",
                        CreatedAt = DbNow()
                    };
                    _db.InboundReturnSlips.Add(returnSlip);

                    // Save slip code changes so PDF generator has data
                    await _db.SaveChangesAsync();

                    // Generate Inbound Return Slip PDF
                    try
                    {
                        returnSlip.PdfUrl = await GenerateReturnSlipPdfAsync(appendix.Order, lpn, returnSlip, PenaltyAmount);
                    }
                    catch (Exception ex)
                    {
                        // Proceed
                    }
                }

                // 3. Mark appendix as EXECUTED
                appendix.Status = Executed;
                appendix.ResolvedAt = DbNow();

                await EnsureNotificationTemplateAsync("NOTI_APPENDIX_EXECUTED");
                await AddNotificationAsync(
                    salesUserId,
                    salesUserId,
                    "NOTI_APPENDIX_EXECUTED",
                    appendix.OrderId,
                    new { Appendix_Number = appendix.AppendixNumber, Tracking_Code = appendix.Order.TrackingCode });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // SignalR
                await _hubContext.Clients.Group("Group_Sales").SendAsync("AppendixExecuted", new
                {
                    appendix.AppendixId,
                    appendix.AppendixNumber,
                    appendix.Status,
                    appendix.OrderId
                });

                var resp = ToResponse(appendix);
                if (wasRejected)
                {
                    var penaltyBill = await _db.PenaltyBills.FirstOrDefaultAsync(pb => pb.OrderId == appendix.OrderId);
                    if (penaltyBill != null)
                    {
                        resp.PenaltyBill = new PenaltyBillResponse
                        {
                            PenaltyBillId = penaltyBill.PenaltyBillId,
                            BillCode = penaltyBill.BillCode,
                            LpnId = penaltyBill.LpnId,
                            OrderId = penaltyBill.OrderId,
                            HandlingFee = penaltyBill.HandlingFee,
                            StorageFee = penaltyBill.StorageFee,
                            TotalAmount = penaltyBill.TotalAmount,
                            Reason = penaltyBill.Reason,
                            IsPaid = penaltyBill.IsPaid,
                            CreatedAt = penaltyBill.CreatedAt,
                            PaidAt = penaltyBill.PaidAt
                        };
                    }
                    var returnSlip = await _db.InboundReturnSlips.FirstOrDefaultAsync(rs => rs.OrderId == appendix.OrderId);
                    if (returnSlip != null)
                    {
                        resp.ReturnSlip = new InboundReturnSlipResponse
                        {
                            ReturnSlipId = returnSlip.ReturnSlipId,
                            OrderId = returnSlip.OrderId,
                            LpnId = returnSlip.LpnId,
                            SlipCode = returnSlip.SlipCode,
                            ReturnedWeightKg = returnSlip.ReturnedWeightKg,
                            ReturnedCbm = returnSlip.ReturnedCbm,
                            ReturnedQty = returnSlip.ReturnedQty,
                            Reason = returnSlip.Reason,
                            PdfUrl = returnSlip.PdfUrl,
                            CreatedAt = returnSlip.CreatedAt
                        };
                    }
                }

                return ApiResponse<ContractAppendixResponse>.SuccessResponse(resp, "Appendix resolution executed successfully");
            });
        }

        public async Task<ApiResponse<ContractAppendixResponse>> GetAppendixByIdAsync(Guid appendixId)
        {
            var appendix = await _db.ContractAppendices.AsNoTracking().FirstOrDefaultAsync(a => a.AppendixId == appendixId);
            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found");

            return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix retrieved");
        }

        public async Task<ApiResponse<ContractAppendixResponse>> GetAppendixByOrderIdAsync(Guid orderId)
        {
            var appendix = await _db.ContractAppendices
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.OrderId == orderId && (a.Status == Draft || a.Status == Sent || a.Status == Accepted || a.Status == Rejected || a.Status == Executed));

            if (appendix == null)
                return ApiResponse<ContractAppendixResponse>.Failure("Contract appendix not found for this order");

            return ApiResponse<ContractAppendixResponse>.SuccessResponse(ToResponse(appendix), "Contract appendix retrieved");
        }

        public async Task<ApiResponse<string>> GetAppendixHtmlAsync(Guid appendixId)
        {
            var html = await _db.ContractAppendices.AsNoTracking()
                .Where(a => a.AppendixId == appendixId)
                .Select(a => a.DraftHtmlContent)
                .FirstOrDefaultAsync();

            if (html == null)
                return ApiResponse<string>.Failure("Contract appendix not found");

            return ApiResponse<string>.SuccessResponse(html, "Contract appendix HTML retrieved");
        }

        public async Task<ApiResponse<int>> ResetAllAppendicesHtmlAsync()
        {
            var appendices = await _db.ContractAppendices.ToListAsync();
            int count = 0;
            foreach (var appendix in appendices)
            {
                var data = await LoadAppendixDataAsync(appendix.OrderId);
                if (data == null) continue;

                try
                {
                    var template = await LoadAppendixTemplateAsync();
                    var html = await RenderAppendixTemplateAsync(template, data, appendix.AppendixNumber, appendix.AdjustedPrice, appendix.Reason ?? string.Empty);
                    appendix.DraftHtmlContent = html;
                    count++;
                }
                catch (Exception)
                {
                    // Ignore and proceed
                }
            }
            await _db.SaveChangesAsync();
            return ApiResponse<int>.SuccessResponse(count, $"Successfully reset and regenerated HTML for {count} contract appendices.");
        }

        private async Task<string> GenerateUniqueAppendixNumberAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var value = $"PL-{DateTime.UtcNow:yyyy}{Random.Shared.Next(0, 999999):D6}";
                if (!await _db.ContractAppendices.AnyAsync(c => c.AppendixNumber == value))
                    return value;
            }
            return $"PL-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }

        private async Task<string> GenerateUniqueInvoiceCodeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var value = $"INV-ADJ-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
                if (!await _db.Invoices.AnyAsync(c => c.InvoiceCode == value))
                    return value;
            }
            return $"INV-ADJ-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }

        private async Task<string> GenerateUniqueSlipCodeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var value = $"XT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
                if (!await _db.InboundReturnSlips.AnyAsync(c => c.SlipCode == value))
                    return value;
            }
            return $"XT-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }

        private async Task<AppendixData?> LoadAppendixDataAsync(Guid orderId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.Customer)
                .Include(o => o.Route)
                .Include(o => o.PickupLocationNavigation)
                .Include(o => o.DestLocationNavigation)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order?.Customer == null)
                return null;

            var contract = await _db.CustomerContracts
                .Where(c => c.OrderId == orderId && c.Status == "ACTIVE")
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            var quotation = await _db.Quotations
                .Where(q => q.OrderId == orderId && q.Status == "ACCEPTED")
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefaultAsync();

            if (quotation == null)
                return null;

            return new AppendixData(order, order.Customer, contract, quotation);
        }

        private async Task<string> LoadAppendixTemplateAsync()
        {
            var path = Path.Combine(_environment.ContentRootPath, "Templates", "ContractAppendixTemplate.html");
            if (!File.Exists(path))
                throw new InvalidOperationException("ContractAppendixTemplate.html was not found");

            return await File.ReadAllTextAsync(path);
        }

        private async Task<decimal> GetSystemConfigDecimalAsync(string key, decimal fallback)
        {
            var value = await _db.SystemConfigs
                .AsNoTracking()
                .Where(c => c.Key == key)
                .Select(c => c.Value)
                .FirstOrDefaultAsync();

            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private async Task<string> RenderAppendixTemplateAsync(string template, AppendixData data, string appendixNumber, decimal adjustedPrice, string reason)
        {
            var now = DateTime.UtcNow;
            
            var volumetricRate = await GetSystemConfigDecimalAsync("VolumetricConversionRate", 250m);
            var actualCbm = data.Order.ActualCbm ?? data.Order.ExpectedCbm;
            var volumetricWeight = Math.Round(actualCbm * volumetricRate, 2);
            var chargeableWeight = Math.Max(Math.Max(data.Order.ActualWeightKg, volumetricWeight), MinChargeableWeightKg);

            var tier = await _db.WeightTiers
                .AsNoTracking()
                .Where(t => t.RouteId == data.Order.RouteId.Value
                            && chargeableWeight >= t.MinWeightKg
                            && (!t.MaxWeightKg.HasValue || chargeableWeight <= t.MaxWeightKg.Value))
                .OrderByDescending(t => t.MinWeightKg)
                .FirstOrDefaultAsync();

            var unitPrice = tier?.PricePerKg ?? 0m;
            var newBasePrice = Math.Round(chargeableWeight * unitPrice, 0);
            var originalBasePrice = data.Quotation.BaseFreight;

            var expectedVolumetricWeight = data.Quotation.VolumetricWeightKg ?? Math.Round(data.Order.ExpectedCbm * volumetricRate, 2);
            var expectedChargeableWeight = data.Quotation.ChargeableWeightKg ?? Math.Max(Math.Max(data.Order.ExpectedWeightKg, expectedVolumetricWeight), MinChargeableWeightKg);

            var weightDiff = data.Order.ExpectedWeightKg > 0
                ? Math.Round((data.Order.ActualWeightKg - data.Order.ExpectedWeightKg) / data.Order.ExpectedWeightKg * 100m, 2)
                : 0m;
            var cbmDiff = data.Order.ExpectedCbm > 0
                ? Math.Round((actualCbm - data.Order.ExpectedCbm) / data.Order.ExpectedCbm * 100m, 2)
                : 0m;
            var volumetricWeightDiff = expectedVolumetricWeight > 0
                ? Math.Round((volumetricWeight - expectedVolumetricWeight) / expectedVolumetricWeight * 100m, 2)
                : 0m;
            var chargeableWeightDiff = expectedChargeableWeight > 0
                ? Math.Round((chargeableWeight - expectedChargeableWeight) / expectedChargeableWeight * 100m, 2)
                : 0m;

            var maxDiff = Math.Max(Math.Abs(weightDiff), Math.Abs(cbmDiff));

            var lpn = await _db.Lpns
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.OrderId == data.Order.OrderId);

            var expectedLength = data.Order.LengthCm;
            var expectedWidth = data.Order.WidthCm;
            var expectedHeight = data.Order.HeightCm;

            var actualLength = lpn?.LengthCm ?? 0m;
            var actualWidth = lpn?.WidthCm ?? 0m;
            var actualHeight = lpn?.HeightCm ?? 0m;

            var lengthDiff = expectedLength > 0
                ? Math.Round((actualLength - expectedLength) / expectedLength * 100m, 2)
                : 0m;
            var widthDiff = expectedWidth > 0
                ? Math.Round((actualWidth - expectedWidth) / expectedWidth * 100m, 2)
                : 0m;
            var heightDiff = expectedHeight > 0
                ? Math.Round((actualHeight - expectedHeight) / expectedHeight * 100m, 2)
                : 0m;

            var replacements = new Dictionary<string, string?>
            {
                ["Appendix_Number"] = appendixNumber,
                ["Day"] = now.Day.ToString("00"),
                ["Month"] = now.Month.ToString("00"),
                ["Year"] = now.Year.ToString(),
                ["Contract_Number"] = data.Contract?.ContractNumber ?? "HD-CHUAKY",
                ["Customer_CompanyName"] = data.Customer.CompanyName,
                ["Customer_Address"] = data.Customer.Address ?? string.Empty,
                ["Customer_TaxCode"] = data.Customer.TaxCode,
                ["Customer_RepName"] = "",
                ["Customer_RepTitle"] = "",
                ["Customer_Phone"] = "",
                ["Customer_Email"] = data.Customer.Email ?? string.Empty,
                ["Tracking_Code"] = data.Order.TrackingCode,
                ["Item_Name"] = data.Order.ItemName,
                ["Expected_Weight"] = data.Order.ExpectedWeightKg.ToString("0.##", CultureInfo.InvariantCulture),
                ["Expected_Cbm"] = data.Order.ExpectedCbm.ToString("0.####", CultureInfo.InvariantCulture),
                ["Actual_Weight"] = data.Order.ActualWeightKg.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Cbm"] = actualCbm.ToString("0.####", CultureInfo.InvariantCulture),
                ["Discrepancy_Percent"] = maxDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Original_Price"] = originalBasePrice.ToString("N0", CultureInfo.InvariantCulture),
                ["Adjusted_Price"] = adjustedPrice.ToString("N0", CultureInfo.InvariantCulture),
                ["New_Price"] = (originalBasePrice + adjustedPrice).ToString("N0", CultureInfo.InvariantCulture),
                ["Reason"] = reason,

                ["Volumetric_Rate"] = volumetricRate.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Volumetric_Weight"] = volumetricWeight.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Chargeable_Weight"] = chargeableWeight.ToString("0.##", CultureInfo.InvariantCulture),
                ["Unit_Price"] = unitPrice.ToString("N0", CultureInfo.InvariantCulture),
                ["New_Base_Price"] = newBasePrice.ToString("N0", CultureInfo.InvariantCulture),
                ["Original_Base_Price"] = originalBasePrice.ToString("N0", CultureInfo.InvariantCulture),

                ["Expected_Qty"] = data.Order.Quantity.ToString(),
                ["Actual_Qty"] = data.Order.Quantity.ToString(),
                ["Packing_Type"] = data.Order.PackingType ?? "Kiện",
                ["Expected_Volumetric_Weight"] = expectedVolumetricWeight.ToString("0.##", CultureInfo.InvariantCulture),
                ["Expected_Chargeable_Weight"] = expectedChargeableWeight.ToString("0.##", CultureInfo.InvariantCulture),

                ["Weight_Diff"] = (weightDiff >= 0 ? "+" : "") + weightDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Cbm_Diff"] = (cbmDiff >= 0 ? "+" : "") + cbmDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Volumetric_Weight_Diff"] = (volumetricWeightDiff >= 0 ? "+" : "") + volumetricWeightDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Chargeable_Weight_Diff"] = (chargeableWeightDiff >= 0 ? "+" : "") + chargeableWeightDiff.ToString("0.##", CultureInfo.InvariantCulture),

                ["Expected_Length"] = expectedLength.ToString("0.##", CultureInfo.InvariantCulture),
                ["Expected_Width"] = expectedWidth.ToString("0.##", CultureInfo.InvariantCulture),
                ["Expected_Height"] = expectedHeight.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Length"] = actualLength.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Width"] = actualWidth.ToString("0.##", CultureInfo.InvariantCulture),
                ["Actual_Height"] = actualHeight.ToString("0.##", CultureInfo.InvariantCulture),
                ["Length_Diff"] = (lengthDiff >= 0 ? "+" : "") + lengthDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Width_Diff"] = (widthDiff >= 0 ? "+" : "") + widthDiff.ToString("0.##", CultureInfo.InvariantCulture),
                ["Height_Diff"] = (heightDiff >= 0 ? "+" : "") + heightDiff.ToString("0.##", CultureInfo.InvariantCulture)
            };

            foreach (var replacement in replacements)
                template = template.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return template;
        }

        private async Task<string> GenerateInvoicePdfAsync(
            TransportOrder order,
            Invoice invoice,
            InvoiceLine line)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "InvoiceTemplate.html");
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("InvoiceTemplate.html was not found");

            var html = await File.ReadAllTextAsync(templatePath);

            var linesRows = $@"
            <tr>
                 <td>{line.ChargeType}</td>
                 <td>{line.Description}</td>
                 <td>{line.Quantity:0.##}</td>
                 <td>{line.UnitPrice.ToString("N0", CultureInfo.InvariantCulture)}</td>
                 <td>{line.Amount.ToString("N0", CultureInfo.InvariantCulture)}</td>
            </tr>";

            var replacements = new Dictionary<string, string?>
            {
                ["Invoice_Code"] = invoice.InvoiceCode,
                ["Issued_Date"] = invoice.IssuedDate.ToString("dd/MM/yyyy"),
                ["Due_Date"] = invoice.DueDate.ToString("dd/MM/yyyy"),
                ["Customer_Name"] = order.Customer?.CompanyName ?? "Khách hàng vãng lai",
                ["Order_Tracking_Code"] = order.TrackingCode,
                ["Warehouse_Name"] = "Kho Proship HCM",
                ["Status"] = "Chưa thanh toán",
                ["Status_Class"] = "status-unpaid",
                ["Sub_Total"] = invoice.SubTotal.ToString("N0", CultureInfo.InvariantCulture),
                ["Tax_Rate"] = "8",
                ["Tax_Amount"] = invoice.TaxAmount.ToString("N0", CultureInfo.InvariantCulture),
                ["Grand_Total"] = invoice.GrandTotal.ToString("N0", CultureInfo.InvariantCulture),
                ["Invoice_Lines_Table_Rows"] = linesRows
            };

            foreach (var replacement in replacements)
                html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return await _pdfService.SaveInvoicePdfAsync(html, invoice.InvoiceCode);
        }

        private async Task<string> GenerateReturnSlipPdfAsync(
            TransportOrder order,
            Lpn lpn,
            InboundReturnSlip slip,
            decimal penaltyAmount)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "ReturnSlipTemplate.html");
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("ReturnSlipTemplate.html was not found");

            var html = await File.ReadAllTextAsync(templatePath);
            var now = DateTime.UtcNow;

            var replacements = new Dictionary<string, string?>
            {
                ["Slip_Code"] = slip.SlipCode,
                ["Day"] = now.Day.ToString("00"),
                ["Month"] = now.Month.ToString("00"),
                ["Year"] = now.Year.ToString(),
                ["Warehouse_Name"] = "Kho Proship HCM",
                ["Tracking_Code"] = order.TrackingCode,
                ["Lpn_Code"] = lpn.LpnCode,
                ["Customer_Name"] = order.Customer?.CompanyName ?? "Khách hàng vãng lai",
                ["Item_Name"] = order.ItemName,
                ["Returned_Qty"] = slip.ReturnedQty.ToString(),
                ["Returned_Weight"] = slip.ReturnedWeightKg.ToString("0.##"),
                ["Returned_Cbm"] = slip.ReturnedCbm.ToString("0.####"),
                ["Reason"] = slip.Reason,
                ["Handling_Fee"] = penaltyAmount.ToString("N0", CultureInfo.InvariantCulture)
            };

            foreach (var replacement in replacements)
                html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return await _pdfService.SaveInboundReturnSlipPdfAsync(html, slip.SlipCode);
        }

        private async Task<Guid?> ResolveCustomerUserIdAsync(Guid? customerId)
        {
            if (!customerId.HasValue) return null;
            var email = await _db.Customers.Where(c => c.CustomerId == customerId.Value).Select(c => c.Email).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(email)) return null;

            return await _db.Users
                .Where(u => u.Email != null && u.Email.ToLower() == email.ToLower())
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task<Guid?> ResolveSalesUserIdAsync()
        {
            return await _db.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null && (u.Role.RoleName.ToLower() == "sales" || u.Role.RoleName.ToLower() == "admin" || u.Role.RoleName.ToLower() == "manager"))
                .Select(u => (Guid?)u.UserId)
                .FirstOrDefaultAsync();
        }

        private async Task EnsureNotificationTemplateAsync(string templateId)
        {
            if (await _db.NotificationTemplates.AnyAsync(t => t.TemplateId == templateId))
                return;

            var typeId = await _db.Messagetypes
                .Where(t => t.TypeName == "ORDER_STATUS")
                .Select(t => (Guid?)t.TypeId)
                .FirstOrDefaultAsync();

            if (!typeId.HasValue)
            {
                var type = new Messagetype
                {
                    TypeId = Guid.NewGuid(),
                    TypeName = "ORDER_STATUS",
                    Description = "Cập nhật trạng thái đơn hàng, báo giá, hợp đồng"
                };
                _db.Messagetypes.Add(type);
                typeId = type.TypeId;
            }

            var (title, body) = templateId switch
            {
                "NOTI_APPENDIX_PENDING_SIGNATURE" => (
                    "Phụ lục hợp đồng {{Appendix_Number}} đang chờ ký",
                    "Phụ lục hợp đồng {{Appendix_Number}} của đơn {{Tracking_Code}} đã được tạo. Vui lòng xem và ký duyệt."),
                "NOTI_APPENDIX_ACCEPTED" => (
                    "Khách hàng đã ký phụ lục {{Appendix_Number}}",
                    "Phụ lục hợp đồng điều chỉnh giá số {{Appendix_Number}} đã được ký duyệt thành công."),
                "NOTI_APPENDIX_REJECTED" => (
                    "Khách hàng từ chối ký phụ lục {{Appendix_Number}}",
                    "Khách hàng đã từ chối phụ lục hợp đồng số {{Appendix_Number}}. Đơn hàng đã chuyển sang hoàn trả."),
                "NOTI_APPENDIX_SENT" => (
                    "Đã gửi phụ lục hợp đồng {{Appendix_Number}}",
                    "Phụ lục hợp đồng số {{Appendix_Number}} của đơn {{Tracking_Code}} đã được gửi thành công cho khách hàng."),
                "NOTI_APPENDIX_EXECUTED" => (
                    "Xử lý phụ lục {{Appendix_Number}} thành công",
                    "Yêu cầu giải quyết chênh lệch cho đơn hàng {{Tracking_Code}} (Phụ lục số {{Appendix_Number}}) đã được thực thi thành công."),
                _ => (
                    "Cập nhật phụ lục hợp đồng {{Appendix_Number}}",
                    "Phụ lục hợp đồng {{Appendix_Number}} có cập nhật mới.")
            };

            _db.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = templateId,
                TypeId = typeId.Value,
                TitleTemplate = title,
                BodyTemplate = body,
                Channel = "IN_APP",
                Status = "ACTIVE"
            });
        }

        private async Task AddNotificationAsync(Guid? userId, Guid? senderId, string templateId, Guid orderId, object parameters)
        {
            if (!userId.HasValue) return;

            // Clean up previous notifications in the discrepancy/appendix flow for this order
            var flowTemplates = new[]
            {
                "NOTI_QC_DISCREPANCY",
                "NOTI_APPENDIX_PENDING_SIGNATURE",
                "NOTI_APPENDIX_ACCEPTED",
                "NOTI_APPENDIX_REJECTED",
                "NOTI_APPENDIX_SENT",
                "NOTI_APPENDIX_EXECUTED"
            };

            var oldNotifications = await _db.Notifications
                .Where(n => n.OrderId == orderId && flowTemplates.Contains(n.TemplateId))
                .ToListAsync();

            if (oldNotifications.Any())
            {
                _db.Notifications.RemoveRange(oldNotifications);
            }

            _db.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = userId.Value,
                SenderId = senderId,
                TemplateId = templateId,
                OrderId = orderId,
                Params = JsonSerializer.Serialize(parameters),
                IsRead = false,
                CreatedAt = DbNow()
            });
        }

        private static ContractAppendixResponse ToResponse(ContractAppendix appendix)
        {
            return new ContractAppendixResponse
            {
                AppendixId = appendix.AppendixId,
                ContractId = appendix.ContractId,
                OrderId = appendix.OrderId,
                AppendixNumber = appendix.AppendixNumber,
                AdjustedPrice = appendix.AdjustedPrice,
                Reason = appendix.Reason,
                Status = appendix.Status,
                DraftHtmlContent = appendix.DraftHtmlContent,
                PdfUrl = appendix.PdfUrl,
                CreatedAt = appendix.CreatedAt,
                SentAt = appendix.SentAt,
                ResolvedAt = appendix.ResolvedAt
            };
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        private sealed record AppendixData(TransportOrder Order, Customer Customer, CustomerContract? Contract, Quotation Quotation);
    }
}
