using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IContractService
    {
        Task<ApiResponse<string>> PreviewContractAsync(Guid orderId);
        Task<ApiResponse<GenerateContractResponse>> GenerateContractAsync(GenerateContractRequest request, Guid salesUserId);
        Task<ApiResponse<ApproveContractResponse>> ApproveContractAsync(Guid contractId, Guid customerId);
    }
}
