using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IAuthService
    {
        Task<ApiResponse<AuthResponseDto>> RegisterAsync(RegisterRequest request);
        Task<ApiResponse<AuthResponseDto>> CreateCustomerAsync(CreateCustomerRequest request);
        Task<ApiResponse<AuthResponseDto>> CreateDriverAsync(CreateDriverRequest request);
        Task<ApiResponse<AuthResponseDto>> CreateLoaderAsync(CreateLoaderRequest request);
        Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequest request);
        Task<ApiResponse<AuthResponseDto>> RefreshTokensAsync(string refreshToken);
        Task<ApiResponse<bool>> LogoutAsync(Guid userId);
        Task<ApiResponse<UserProfileDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request);
        Task<ApiResponse<bool>> SoftDeleteUserAsync(Guid targetUserId);
        Task<ApiResponse<DriverDto>> UpdateDriverAsync(Guid driverId, UpdateDriverInfoRequest request);
        Task<ApiResponse<List<RoleDto>>> GetAllRolesAsync();
    }
}
