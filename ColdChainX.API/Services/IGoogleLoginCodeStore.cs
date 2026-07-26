using ColdChainX.Application.DTOs.GoogleAuth;

namespace ColdChainX.API.Services
{
    public interface IGoogleLoginCodeStore
    {
        Task StoreAsync(
            string code,
            GoogleLoginResponse authentication,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default);

        Task<GoogleLoginResponse?> TakeAsync(
            string code,
            CancellationToken cancellationToken = default);
    }
}
