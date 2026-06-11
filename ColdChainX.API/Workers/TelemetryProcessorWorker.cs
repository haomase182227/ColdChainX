using System.Threading.Channels;
using ColdChainX.API.Models;
using ColdChainX.API.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Workers;

public sealed class TelemetryProcessorWorker : BackgroundService
{
    private readonly Channel<TelemetryData> _telemetryChannel;
    private readonly RedisService _redisService;
    private readonly ILogger<TelemetryProcessorWorker> _logger;

    public TelemetryProcessorWorker(
        Channel<TelemetryData> telemetryChannel,
        RedisService redisService,
        ILogger<TelemetryProcessorWorker> logger)
    {
        _telemetryChannel = telemetryChannel;
        _redisService = redisService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var data in _telemetryChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var currentCount = await _redisService.AddTelemetryAndTrimAsync(data.DeviceId, data);
                var redisKey = $"temp_history:{data.DeviceId}";

                _logger.LogInformation(
                    "Telemetry buffered in Redis device={DeviceId} redisKey={RedisKey} count={Count}",
                    data.DeviceId,
                    redisKey,
                    currentCount);
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
                    data.DeviceId);
            }
        }
    }
}
