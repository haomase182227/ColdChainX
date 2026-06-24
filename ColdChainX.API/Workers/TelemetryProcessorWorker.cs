using System.Threading.Channels;
using ColdChainX.API.Models;
using ColdChainX.API.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Workers;

public sealed class TelemetryProcessorWorker : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 50;

    private readonly Channel<TelemetryData> _telemetryChannel;
    private readonly RedisService _redisService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryProcessorWorker> _logger;

    public TelemetryProcessorWorker(
        Channel<TelemetryData> telemetryChannel,
        RedisService redisService,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryProcessorWorker> logger)
    {
        _telemetryChannel = telemetryChannel;
        _redisService = redisService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TelemetryData>(BatchSize);
        var nextFlushAt = DateTimeOffset.UtcNow + FlushInterval;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var waitForData = _telemetryChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();
                    var waitForFlush = Task.Delay(
                        nextFlushAt - DateTimeOffset.UtcNow > TimeSpan.Zero
                            ? nextFlushAt - DateTimeOffset.UtcNow
                            : TimeSpan.Zero,
                        stoppingToken);

                    var completed = await Task.WhenAny(waitForData, waitForFlush);
                    if (completed == waitForData && await waitForData)
                    {
                        while (batch.Count < BatchSize && _telemetryChannel.Reader.TryRead(out var data))
                        {
                            batch.Add(data);
                            var currentCount = await _redisService.AddTelemetryAndTrimAsync(data.DeviceId, data);

                            _logger.LogInformation(
                                "Telemetry buffered in Redis device={DeviceId} redisKey={RedisKey} count={Count}",
                                data.DeviceId,
                                $"temp_history:{data.DeviceId}",
                                currentCount);
                        }
                    }

                    if (batch.Count >= BatchSize || DateTimeOffset.UtcNow >= nextFlushAt)
                    {
                        if (batch.Count > 0)
                        {
                            await FlushBatchAsync(batch, stoppingToken);
                            batch.Clear();
                        }
                        nextFlushAt = DateTimeOffset.UtcNow + FlushInterval;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to buffer telemetry in Redis device={DeviceId}",
                        batch.LastOrDefault()?.DeviceId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(List<TelemetryData> batch, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var monitoringService = scope.ServiceProvider.GetRequiredService<IColdChainMonitoringService>();
        await monitoringService.ProcessTelemetryBatchAsync(batch, cancellationToken);

        _logger.LogInformation("Telemetry batch flushed to monitoring pipeline count={Count}", batch.Count);
    }
}
