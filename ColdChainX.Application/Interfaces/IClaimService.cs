using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Claim;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IClaimService
    {
        Task<ApiResponse<ClaimResponse>> CreateClaimAsync(CreateClaimRequest request, Guid userId, Guid? customerId = null, bool isCustomer = false);
        Task<ApiResponse<bool>> ResolveClaimAsync(Guid claimId, ResolveClaimRequest request, Guid userId);
        Task<ApiResponse<ClaimResponse>> GetClaimByIdAsync(Guid claimId);
        Task<ApiResponse<PagedResult<ClaimResponse>>> GetPagedClaimsAsync(Guid? orderId, string? status, int pageNumber, int pageSize);
        Task<ApiResponse<bool>> CompleteClaimPayoutAsync(Guid claimId, CompleteClaimPayoutRequest request, Guid userId);
    }
}
