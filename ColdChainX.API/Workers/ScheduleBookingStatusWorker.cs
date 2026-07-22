using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.API.Workers;

public class ScheduleBookingStatusWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private readonly ILogger<ScheduleBookingStatusWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ScheduleBookingStatusWorker(
        ILogger<ScheduleBookingStatusWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseExpiredBookingsAsync(stoppingToken);
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close route schedules after cut-off.");
                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
    }

    private async Task CloseExpiredBookingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var vietnamNow = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);

        var activeSchedules = await db.RouteSchedules
            .Where(schedule => schedule.Status == "ACTIVE")
            .ToListAsync(cancellationToken);

        var expiredSchedules = activeSchedules
            .Where(schedule => schedule.DepartureDate.Date.Add(schedule.CutOffTime) <= vietnamNow)
            .ToList();

        if (expiredSchedules.Count == 0)
            return;

        foreach (var schedule in expiredSchedules)
            schedule.Status = "INACTIVE";

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Closed booking for {Count} route schedule(s) after cut-off.",
            expiredSchedules.Count);
    }
}
