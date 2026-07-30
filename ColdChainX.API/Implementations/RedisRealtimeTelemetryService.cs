using ColdChainX.API.Services;
using ColdChainX.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ColdChainX.API.Implementations;

/// <summary>
/// Implementation đọc tọa độ GPS real-time trực tiếp từ Redis hot cache (không qua SQL Database).
/// Đảm bảo độ trễ gần bằng 0 so với tín hiệu IoT gốc.
/// </summary>
public sealed class RedisRealtimeTelemetryService : IRealtimeTelemetryService
{
    private readonly RedisService _redisService;
    private readonly ILogger<RedisRealtimeTelemetryService> _logger;

    public RedisRealtimeTelemetryService(RedisService redisService, ILogger<RedisRealtimeTelemetryService> logger)
    {
        _redisService = redisService;
        _logger = logger;
    }

    public async Task<RealtimeGpsPosition?> GetLatestGpsPositionAsync(string deviceCode)
    {
        try
        {
            var latestData = await _redisService.GetLatestAsync(deviceCode);
            if (latestData == null)
            {
                _logger.LogDebug("No telemetry data in Redis for device {DeviceCode}", deviceCode);
                return null;
            }

            if (latestData.Lat == 0 && latestData.Lon == 0)
            {
                _logger.LogDebug("Device {DeviceCode} has (0,0) coordinates in Redis, skipping.", deviceCode);
                return null;
            }

            _logger.LogInformation(
                "Real-time GPS from Redis: device={DeviceCode} lat={Lat} lon={Lon} timestamp={Timestamp}",
                deviceCode, latestData.Lat, latestData.Lon, latestData.Timestamp);

            return new RealtimeGpsPosition
            {
                Latitude = (decimal)latestData.Lat,
                Longitude = (decimal)latestData.Lon,
                Timestamp = latestData.Timestamp,
                DeviceCode = deviceCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve real-time GPS from Redis for device {DeviceCode}. Falling back to SQL.", deviceCode);
            return null;
        }
    }
}
