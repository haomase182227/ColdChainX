using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ColdChainX.Application.DTOs.GoogleAuth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Services
{
    public class GoogleLoginCodeStore : IGoogleLoginCodeStore
    {
        private const string RedisKeyPrefix = "google_login_code:";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ConcurrentDictionary<string, StoredGoogleLogin> _developmentCodes = new();
        private readonly RedisService _redisService;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<GoogleLoginCodeStore> _logger;

        public GoogleLoginCodeStore(
            RedisService redisService,
            IHostEnvironment environment,
            ILogger<GoogleLoginCodeStore> logger)
        {
            _redisService = redisService;
            _environment = environment;
            _logger = logger;
        }

        public async Task StoreAsync(
            string code,
            GoogleLoginResponse authentication,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BuildKey(code);
            var entry = new StoredGoogleLogin
            {
                UserId = authentication.User.UserId,
                Authentication = authentication,
                ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime)
            };

            if (_environment.IsDevelopment())
            {
                RemoveExpiredDevelopmentCodes();
                _developmentCodes[key] = entry;
                return;
            }

            var payload = JsonSerializer.Serialize(entry, JsonOptions);
            await _redisService.SetStringAsync(key, payload, lifetime);
        }

        public async Task<GoogleLoginResponse?> TakeAsync(
            string code,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BuildKey(code);
            StoredGoogleLogin? entry;

            if (_environment.IsDevelopment())
            {
                if (!_developmentCodes.TryRemove(key, out entry))
                    return null;
            }
            else
            {
                var payload = await _redisService.GetAndDeleteStringAsync(key);
                if (string.IsNullOrWhiteSpace(payload))
                    return null;

                try
                {
                    entry = JsonSerializer.Deserialize<StoredGoogleLogin>(payload, JsonOptions);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "A Google one-time login code contained an invalid payload.");
                    return null;
                }
            }

            if (entry == null || entry.ExpiresAt <= DateTimeOffset.UtcNow)
                return null;

            return entry.Authentication;
        }

        private void RemoveExpiredDevelopmentCodes()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in _developmentCodes)
            {
                if (item.Value.ExpiresAt <= now)
                    _developmentCodes.TryRemove(item.Key, out _);
            }
        }

        private static string BuildKey(string code)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            return RedisKeyPrefix + Convert.ToHexString(hash).ToLowerInvariant();
        }

        private sealed class StoredGoogleLogin
        {
            public Guid UserId { get; set; }
            public GoogleLoginResponse Authentication { get; set; } = null!;
            public DateTimeOffset ExpiresAt { get; set; }
        }
    }
}
