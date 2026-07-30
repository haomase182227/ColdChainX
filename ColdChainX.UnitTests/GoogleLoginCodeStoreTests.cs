using ColdChainX.API.Services;
using ColdChainX.Application.DTOs.GoogleAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ColdChainX.UnitTests
{
    public class GoogleLoginCodeStoreTests : IAsyncDisposable
    {
        private readonly RedisService _redisService;
        private readonly GoogleLoginCodeStore _store;

        public GoogleLoginCodeStoreTests()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Redis"] = "localhost:6379,abortConnect=false"
                })
                .Build();

            _redisService = new RedisService(
                configuration,
                NullLogger<RedisService>.Instance);
            _store = new GoogleLoginCodeStore(
                _redisService,
                new DevelopmentHostEnvironment(),
                NullLogger<GoogleLoginCodeStore>.Instance);
        }

        [Fact]
        public async Task TakeAsync_RemovesCodeAfterFirstSuccessfulExchange()
        {
            var authentication = CreateAuthentication();
            await _store.StoreAsync("single-use-code", authentication, TimeSpan.FromMinutes(2));

            var firstResult = await _store.TakeAsync("single-use-code");
            var secondResult = await _store.TakeAsync("single-use-code");

            Assert.Same(authentication, firstResult);
            Assert.Null(secondResult);
        }

        [Fact]
        public async Task TakeAsync_RejectsExpiredCodeAndRemovesIt()
        {
            await _store.StoreAsync(
                "expired-code",
                CreateAuthentication(),
                TimeSpan.FromMilliseconds(-1));

            var result = await _store.TakeAsync("expired-code");
            var repeatedResult = await _store.TakeAsync("expired-code");

            Assert.Null(result);
            Assert.Null(repeatedResult);
        }

        public async ValueTask DisposeAsync()
        {
            await _redisService.DisposeAsync();
        }

        private static GoogleLoginResponse CreateAuthentication()
        {
            return new GoogleLoginResponse
            {
                Token = "application-jwt",
                RefreshToken = "application-refresh-token",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                User = new GoogleLoginUserDto
                {
                    UserId = Guid.NewGuid(),
                    Username = "google.user@example.com",
                    FullName = "Google User",
                    Email = "google.user@example.com"
                }
            };
        }

        private sealed class DevelopmentHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "ColdChainX.UnitTests";
            public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
