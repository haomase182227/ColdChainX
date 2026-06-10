using System.Text.Json;
using ColdChainX.API.Models;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace ColdChainX.API.Services;

public sealed class RedisService : IAsyncDisposable
{
    private const int MaxHistoryLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly Lazy<ConnectionMultiplexer> _redis;

    public RedisService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? "localhost:6379";

        _redis = new Lazy<ConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(connectionString),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<long> AddTelemetryAndTrimAsync(string deviceId, TelemetryData data)
    {
        var key = BuildHistoryKey(deviceId);
        var database = _redis.Value.GetDatabase();
        var payload = JsonSerializer.Serialize(data, JsonOptions);

        await database.ListLeftPushAsync(key, payload);
        await database.ListTrimAsync(key, 0, MaxHistoryLength - 1);

        return await database.ListLengthAsync(key);
    }

    public async Task<IReadOnlyList<TelemetryData>> GetHistoryAsync(string deviceId)
    {
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
        if (!_redis.IsValueCreated)
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
