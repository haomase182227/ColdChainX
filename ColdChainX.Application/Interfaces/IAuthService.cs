using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IAuthService
    {
        Task<ApiResponse<AuthResponseDto>> RegisterAsync(RegisterRequest request);
        Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequest request);
        Task<ApiResponse<AuthResponseDto>> RefreshTokensAsync(string refreshToken);
        Task<ApiResponse<bool>> LogoutAsync(Guid userId);
    }
}
