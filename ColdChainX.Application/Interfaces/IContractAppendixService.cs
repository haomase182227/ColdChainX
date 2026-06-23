using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IContractAppendixService
    {
        Task<ApiResponse<string>> PreviewAppendixAsync(Guid orderId, decimal adjustedPrice, string reason);
        Task<ApiResponse<ContractAppendixResponse>> GenerateAppendixAsync(Guid orderId, decimal? adjustedPrice, string reason, Guid salesUserId);
        Task<ApiResponse<ContractAppendixResponse>> UpdateAppendixDraftAsync(Guid appendixId, string editedHtmlContent, Guid salesUserId);
        Task<ApiResponse<ContractAppendixResponse>> SendAppendixAsync(Guid appendixId, Guid salesUserId);
        Task<ApiResponse<ContractAppendixResponse>> AcceptAppendixAsync(Guid appendixId, Guid customerId);
        Task<ApiResponse<ContractAppendixResponse>> RejectAppendixAsync(Guid appendixId, Guid customerId);
        Task<ApiResponse<ContractAppendixResponse>> ExecuteAppendixResolutionAsync(Guid appendixId, Guid salesUserId);
        Task<ApiResponse<ContractAppendixResponse>> GetAppendixByIdAsync(Guid appendixId);
        Task<ApiResponse<ContractAppendixResponse>> GetAppendixByOrderIdAsync(Guid orderId);
        Task<ApiResponse<string>> GetAppendixHtmlAsync(Guid appendixId);
    }
}
