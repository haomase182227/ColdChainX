using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IContractService
    {
        Task<ApiResponse<ContractInfoResponse>> GetContractByIdAsync(Guid contractId);
        Task<ApiResponse<ContractInfoResponse>> GetContractByOrderIdAsync(Guid orderId);
        Task<ApiResponse<string>> GetContractHtmlAsync(Guid contractId);
        Task<ApiResponse<string>> PreviewContractAsync(Guid orderId);
        Task<ApiResponse<GenerateContractResponse>> GenerateContractAsync(GenerateContractRequest request, Guid salesUserId);
        Task<ApiResponse<GenerateContractResponse>> UpdateContractDraftAsync(Guid contractId, UpdateContractDraftRequest request, Guid salesUserId);
        Task<ApiResponse<ContractInfoResponse>> SendContractAsync(Guid contractId, Guid salesUserId);
        Task<ApiResponse<UploadSignedContractResponse>> UploadSignedContractAsync(Guid contractId, UploadSignedContractRequest request, Guid customerId, string baseUrl);
        Task<ApiResponse<ApproveContractResponse>> VerifyContractAsync(Guid contractId, Guid salesUserId);
        Task<ApiResponse<ApproveContractResponse>> ApproveContractAsync(Guid contractId, Guid customerId);
    }
}
