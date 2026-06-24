using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Invoices;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    /// <summary>
    /// Service for querying invoices and checking client permissions.
    /// </summary>
    public class InvoiceService : IInvoiceService
    {
        private readonly IApplicationDbContext _db;

        public InvoiceService(IApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<PagedResult<InvoiceResponse>>> GetInvoicesAsync(
            Guid? customerId,
            string? status,
            int pageNumber,
            int pageSize)
        {
            var query = _db.Invoices
                .Include(i => i.InvoiceLines)
                .AsNoTracking();

            // Filter by CustomerId if specified (forced for Customer role, optional for Admin/Manager)
            if (customerId.HasValue)
            {
                query = query.Where(i => i.CustomerId == customerId.Value);
            }

            // Filter by Status if specified
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(i => i.Status == status.Trim());
            }

            // Order by most recent issued date and creation date
            query = query.OrderByDescending(i => i.IssuedDate)
                         .ThenByDescending(i => i.CreatedAt);

            var totalRecords = await query.CountAsync();

            var invoices = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var mappedInvoices = invoices.Select(MapToInvoiceResponse).ToList();

            var pagedResult = PagedResult<InvoiceResponse>.Create(mappedInvoices, totalRecords, pageNumber, pageSize);

            return ApiResponse<PagedResult<InvoiceResponse>>.SuccessResponse(pagedResult, "Invoices retrieved successfully.");
        }

        public async Task<ApiResponse<InvoiceResponse>> GetInvoiceByIdAsync(
            Guid invoiceId,
            Guid? customerId,
            string userRole)
        {
            var invoice = await _db.Invoices
                .Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice == null)
            {
                return ApiResponse<InvoiceResponse>.Failure("Invoice not found.");
            }

            // Authorization: If role is Customer, the invoice must belong to their CustomerId
            if (userRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                if (!customerId.HasValue || invoice.CustomerId != customerId.Value)
                {
                    return ApiResponse<InvoiceResponse>.Failure("Access denied to this invoice.");
                }
            }

            var response = MapToInvoiceResponse(invoice);
            return ApiResponse<InvoiceResponse>.SuccessResponse(response, "Invoice retrieved successfully.");
        }

        public async Task<ApiResponse<List<InvoiceResponse>>> GetInvoicesByOrderIdAsync(
            Guid orderId,
            Guid? customerId,
            string userRole)
        {
            // Verify if order exists
            var order = await _db.TransportOrders.FindAsync(orderId);
            if (order == null)
            {
                return ApiResponse<List<InvoiceResponse>>.Failure("Transport order not found.");
            }

            // Authorization check for Customer role
            if (userRole.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            {
                if (!customerId.HasValue || order.CustomerId != customerId.Value)
                {
                    return ApiResponse<List<InvoiceResponse>>.Failure("Access denied to this order's invoices.");
                }
            }

            // Get invoices that have lines linking to this OrderId
            var invoices = await _db.Invoices
                .Include(i => i.InvoiceLines)
                .Where(i => i.InvoiceLines.Any(il => il.OrderId == orderId))
                .OrderByDescending(i => i.IssuedDate)
                .ToListAsync();

            var response = invoices.Select(MapToInvoiceResponse).ToList();
            return ApiResponse<List<InvoiceResponse>>.SuccessResponse(response, "Order invoices retrieved successfully.");
        }

        private static InvoiceResponse MapToInvoiceResponse(Invoice invoice)
        {
            return new InvoiceResponse
            {
                InvoiceId = invoice.InvoiceId,
                InvoiceCode = invoice.InvoiceCode,
                CustomerId = invoice.CustomerId,
                VatInvoiceNo = invoice.VatInvoiceNo,
                PdfUrl = invoice.PdfUrl,
                SubTotal = invoice.SubTotal,
                TaxRate = invoice.TaxRate,
                TaxAmount = invoice.TaxAmount,
                DeductionAmount = invoice.DeductionAmount,
                GrandTotal = invoice.GrandTotal,
                PaidAmount = invoice.PaidAmount,
                IssuedDate = invoice.IssuedDate,
                DueDate = invoice.DueDate,
                Status = invoice.Status,
                CreatedAt = invoice.CreatedAt,
                InvoiceLines = invoice.InvoiceLines.Select(il => new InvoiceLineResponse
                {
                    LineId = il.LineId,
                    InvoiceId = il.InvoiceId,
                    OrderId = il.OrderId,
                    ChargeType = il.ChargeType,
                    Description = il.Description,
                    Quantity = il.Quantity,
                    UnitPrice = il.UnitPrice,
                    Amount = il.Amount,
                    TaxRate = il.TaxRate
                }).ToList()
            };
        }
    }
}
