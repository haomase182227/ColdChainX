using System.Net;
using System.Text;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Shared.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ColdChainX.UnitTests
{
    public class GoogleOAuthInfrastructureTests
    {
        [Fact]
        public async Task GoogleIdTokenValidator_WithMalformedToken_ReturnsNull()
        {
            var validator = new GoogleIdTokenValidator(
                Options.Create(new GoogleAuthSettings
                {
                    ClientId = "test-client-id.apps.googleusercontent.com"
                }),
                NullLogger<GoogleIdTokenValidator>.Instance);

            var result = await validator.ValidateAsync("not-a-google-jwt");

            Assert.Null(result);
        }

        [Fact]
        public async Task GoogleOAuthClient_ExchangesAuthorizationCodeAndReturnsIdToken()
        {
            var handler = new RecordingHttpMessageHandler();
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://oauth2.googleapis.com/")
            };
            var client = new GoogleOAuthClient(
                new FakeHttpClientFactory(httpClient),
                Options.Create(new GoogleAuthSettings
                {
                    ClientId = "test-client-id",
                    ClientSecret = "test-client-secret"
                }),
                NullLogger<GoogleOAuthClient>.Instance);

            var idToken = await client.ExchangeCodeForIdTokenAsync(
                "authorization-code",
                "https://api.example.com/api/auth/google/callback");

            Assert.Equal("google-id-token", idToken);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal(
                "https://oauth2.googleapis.com/token",
                handler.RequestUri?.AbsoluteUri);
            Assert.Contains("client_id=test-client-id", handler.FormBody);
            Assert.Contains("client_secret=test-client-secret", handler.FormBody);
            Assert.Contains("code=authorization-code", handler.FormBody);
            Assert.Contains("grant_type=authorization_code", handler.FormBody);
        }

        private sealed class FakeHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;

            public FakeHttpClientFactory(HttpClient client)
            {
                _client = client;
            }

            public HttpClient CreateClient(string name) => _client;
        }

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            public HttpMethod? Method { get; private set; }
            public Uri? RequestUri { get; private set; }
            public string FormBody { get; private set; } = string.Empty;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Method = request.Method;
                RequestUri = request.RequestUri;
                FormBody = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"id_token":"google-id-token"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }
        }
    }
}
