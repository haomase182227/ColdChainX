using ColdChainX.Application.DTOs.Quotations;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IQuotationService
    {
        Task<ApiResponse<IReadOnlyCollection<QuotationResponse>>> GetQuotationsAsync();
        Task<ApiResponse<QuotationResponse>> GetQuotationByIdAsync(Guid quoteId);
        Task<ApiResponse<IReadOnlyCollection<QuotationResponse>>> GetQuotationsByOrderAsync(Guid orderId);
        Task<ApiResponse<QuotationResponse>> CreateQuotationAsync(CreateQuotationRequest request);
        Task<ApiResponse<AcceptQuotationResponse>> AcceptQuotationAsync(Guid quoteId, AcceptQuotationRequest request);
    }
}
