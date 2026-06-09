using ColdChainX.Application.DTOs.Quotations;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IQuotationService
    {
        Task<ApiResponse<PagedResult<QuotationResponse>>> GetQuotationsAsync(int pageNumber, int pageSize);
        Task<ApiResponse<QuotationResponse>> GetQuotationByIdAsync(Guid quoteId);
        Task<ApiResponse<PagedResult<QuotationResponse>>> GetQuotationsByOrderAsync(Guid orderId, int pageNumber, int pageSize);
        Task<ApiResponse<QuotationResponse>> CreateQuotationAsync(CreateQuotationRequest request, Guid salesUserId);
        Task<ApiResponse<AcceptQuotationResponse>> AcceptQuotationAsync(Guid quoteId, AcceptQuotationRequest request);
    }
}
