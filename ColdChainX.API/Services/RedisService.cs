using System.Text.Json;
using ColdChainX.API.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ColdChainX.API.Services;

public sealed class RedisService : IAsyncDisposable
{
    private const int MaxHistoryLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly Lazy<ConnectionMultiplexer>? _redis;
    private readonly ILogger<RedisService> _logger;
    private readonly bool _isAvailable;

    public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
    {
        _logger = logger;

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis")
            ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
            ?? configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Redis connection string is missing. Redis features will be disabled.");
            _isAvailable = false;
            return;
        }

        try
        {
            _redis = new Lazy<ConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(connectionString),
                LazyThreadSafetyMode.ExecutionAndPublication);

            var ping = _redis.Value.GetDatabase().Ping();
            _logger.LogInformation("Redis connected successfully. Ping={PingMs}ms", ping.TotalMilliseconds);
            _isAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Redis at '{ConnectionString}'. Redis features will be disabled.", connectionString);
            _redis = null;
            _isAvailable = false;
        }
    }

    public bool IsAvailable => _isAvailable;

    public async Task<long> AddTelemetryAndTrimAsync(string deviceId, TelemetryData data)
    {
        if (!_isAvailable || _redis is null)
        {
            _logger.LogDebug("Redis unavailable, skipping telemetry write for device {DeviceId}.", deviceId);
            return 0;
        }

        var key = BuildHistoryKey(deviceId);
        var database = _redis.Value.GetDatabase();
        var payload = JsonSerializer.Serialize(data, JsonOptions);

        await database.ListLeftPushAsync(key, payload);
        await database.ListTrimAsync(key, 0, MaxHistoryLength - 1);

        return await database.ListLengthAsync(key);
    }

    public async Task<IReadOnlyList<TelemetryData>> GetHistoryAsync(string deviceId)
    {
        if (!_isAvailable || _redis is null)
        {
            _logger.LogDebug("Redis unavailable, returning empty history for device {DeviceId}.", deviceId);
            return Array.Empty<TelemetryData>();
        }

        var key = BuildHistoryKey(deviceId);
        var values = await _redis.Value.GetDatabase().ListRangeAsync(key, 0, MaxHistoryLength - 1);
        var history = new List<TelemetryData>(values.Length);

        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<TelemetryData>(value!, JsonOptions);
            if (item is not null)
            {
                history.Add(item);
            }
        }

        return history;
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is null || !_redis.IsValueCreated)
        {
            return;
        }

        await _redis.Value.DisposeAsync();
    }

    private static string BuildHistoryKey(string deviceId)
    {
        return $"temp_history:{deviceId}";
    }
}
