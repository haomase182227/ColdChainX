using ColdChainX.Application.DTOs.GoogleAuth;

namespace ColdChainX.Application.Interfaces
{
    public interface IGoogleIdTokenValidator
    {
        Task<VerifiedGoogleUserDto?> ValidateAsync(
            string idToken,
            CancellationToken cancellationToken = default);
    }
}
