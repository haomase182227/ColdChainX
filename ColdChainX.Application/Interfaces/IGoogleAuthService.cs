using ColdChainX.Application.DTOs.GoogleAuth;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IGoogleAuthService
    {
        Task<ApiResponse<GoogleLoginResponse>> AuthenticateAsync(
            string? idToken,
            CancellationToken cancellationToken = default);
    }
}
