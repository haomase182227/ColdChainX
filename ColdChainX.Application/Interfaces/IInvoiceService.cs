using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Invoices;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    /// <summary>
    /// Contract for invoice-related business logic.
    /// </summary>
    public interface IInvoiceService
    {
        Task<ApiResponse<PagedResult<InvoiceResponse>>> GetInvoicesAsync(Guid? customerId, string? status, int pageNumber, int pageSize);
        Task<ApiResponse<InvoiceResponse>> GetInvoiceByIdAsync(Guid invoiceId, Guid? customerId, string userRole);
        Task<ApiResponse<List<InvoiceResponse>>> GetInvoicesByOrderIdAsync(Guid orderId, Guid? customerId, string userRole);
    }
}
