using ColdChainX.API.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Workers;

public sealed class IotWatchdogWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisService _redisService;
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly ILogger<IotWatchdogWorker> _logger;

    public IotWatchdogWorker(
        IServiceScopeFactory scopeFactory,
        RedisService redisService,
        IHubContext<MonitoringHub> hubContext,
        ILogger<IotWatchdogWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _redisService = redisService;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IoT watchdog scan failed.");
            }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var trips = await db.MasterTrips
            .Include(t => t.Vehicle)
                .ThenInclude(v => v!.IotDevices)
            .Where(t => t.Status == "IN_TRANSIT" && t.VehicleId != null)
            .ToListAsync(cancellationToken);

        foreach (var trip in trips)
        {
            var device = trip.Vehicle?.IotDevices.FirstOrDefault();
            if (device == null)
            {
                continue;
            }

            var redisDeviceKey = string.IsNullOrWhiteSpace(device.DeviceCode)
                ? device.DeviceId.ToString()
                : device.DeviceCode;
            var latest = await _redisService.GetLatestAsync(redisDeviceKey);
            var latestTimestamp = latest?.Timestamp.UtcDateTime ?? device.LastPingTime;
            if (!latestTimestamp.HasValue)
            {
                await RaiseConnectionLostAsync(db, trip, device, cancellationToken);
                continue;
            }

            var delay = DateTime.UtcNow - DateTime.SpecifyKind(latestTimestamp.Value, DateTimeKind.Utc);
            if (delay > OfflineThreshold)
            {
                await RaiseConnectionLostAsync(db, trip, device, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RaiseConnectionLostAsync(
        ApplicationDbContext db,
        MasterTrip trip,
        IotDevice device,
        CancellationToken cancellationToken)
    {
        var alreadyOpen = await db.AlertLogs.AnyAsync(a =>
            a.TripId == trip.TripId &&
            a.AlertType == "CONNECTION_LOST" &&
            a.Status == "NEW",
            cancellationToken);

        if (!alreadyOpen)
        {
            db.AlertLogs.Add(new AlertLog
            {
                AlertId = Guid.NewGuid(),
                TripId = trip.TripId,
                AlertType = "CONNECTION_LOST",
                Latitude = 0,
                Longitude = 0,
                Status = "NEW",
                CreatedAt = DateTime.UtcNow
            });
        }

        device.Status = "OFFLINE";

        await _hubContext.Clients
            .Group(MonitoringHub.BuildTripGroup(trip.TripId))
            .SendAsync("ReceiveAlert", new
            {
                TripId = trip.TripId,
                AlertType = "CONNECTION_LOST",
                Message = $"IoT device {device.DeviceCode ?? device.DeviceId.ToString()} has no signal for more than 5 minutes.",
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
    }
}
