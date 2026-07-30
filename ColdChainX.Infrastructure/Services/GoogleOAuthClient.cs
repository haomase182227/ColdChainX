using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColdChainX.Infrastructure.Services
{
    public class GoogleOAuthClient : IGoogleOAuthClient
    {
        public const string HttpClientName = "GoogleOAuth";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly GoogleAuthSettings _settings;
        private readonly ILogger<GoogleOAuthClient> _logger;

        public GoogleOAuthClient(
            IHttpClientFactory httpClientFactory,
            IOptions<GoogleAuthSettings> options,
            ILogger<GoogleOAuthClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = options.Value;
            _logger = logger;
        }

        public async Task<string?> ExchangeCodeForIdTokenAsync(
            string code,
            string redirectUri,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.ClientId) ||
                string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "Google OAuth ClientId or ClientSecret is not configured.");
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(
                "token",
                new FormUrlEncodedContent(form),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Google authorization-code exchange failed with status {StatusCode}.",
                    (int)response.StatusCode);
                throw new ExternalServiceException("Google authorization-code exchange failed.");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
                cancellationToken: cancellationToken);

            return tokenResponse?.IdToken;
        }

        private sealed class GoogleTokenResponse
        {
            [JsonPropertyName("id_token")]
            public string? IdToken { get; set; }
        }
    }
}
