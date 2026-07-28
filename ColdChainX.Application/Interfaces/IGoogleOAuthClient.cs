namespace ColdChainX.Application.Interfaces
{
    public interface IGoogleOAuthClient
    {
        Task<string?> ExchangeCodeForIdTokenAsync(
            string code,
            string redirectUri,
            CancellationToken cancellationToken = default);
    }
}
