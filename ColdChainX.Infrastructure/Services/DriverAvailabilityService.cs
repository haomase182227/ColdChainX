using System;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

/// <summary>
/// Driving-hour limit enforcement using calendar windows:
///   - Daily limit:  10h within a UTC calendar day.
///   - Weekly limit: 48h within a Mon–Sun calendar week.
/// When a limit is exceeded the driver is moved to RELAX; the restriction lifts
/// automatically once the calendar day/week rolls over (see <see cref="ReconcileStatusAsync"/>).
/// </summary>
public class DriverAvailabilityService : IDriverAvailabilityService
{
    private readonly ApplicationDbContext _context;

    public const decimal MaxDailyHours = 10m;
    public const decimal MaxWeeklyHours = 48m;

    private const string StatusRelax = "RELAX";
    private const string StatusAvailable = "ACTIVE";

    public DriverAvailabilityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DriverAvailability> CheckAsync(Guid driverId, decimal additionalHours, DateOnly day)
    {
        var (dayHours, weekHours) = await SumHoursAsync(driverId, day);

        var result = new DriverAvailability
        {
            DriverId = driverId,
            DayHours = dayHours,
            WeekHours = weekHours,
            MaxDailyHours = MaxDailyHours,
            MaxWeeklyHours = MaxWeeklyHours,
            CanAssign = true
        };

        if (dayHours + additionalHours > MaxDailyHours)
        {
            result.CanAssign = false;
            result.Reason = $"Vượt giới hạn lái xe trong ngày: {dayHours:F1}h + {additionalHours:F1}h > {MaxDailyHours:F0}h/ngày.";
        }
        else if (weekHours + additionalHours > MaxWeeklyHours)
        {
            result.CanAssign = false;
            result.Reason = $"Vượt giới hạn lái xe trong tuần: {weekHours:F1}h + {additionalHours:F1}h > {MaxWeeklyHours:F0}h/tuần.";
        }

        return result;
    }

    public async Task RecordWorkAsync(Guid driverId, Guid tripId, decimal hours, DateOnly day)
    {
        _context.DriverWorkLogs.Add(new DriverWorkLog
        {
            WorkLogId = Guid.NewGuid(),
            DriverId = driverId,
            TripId = tripId,
            WorkDate = day,
            DrivingHours = hours,
            CreatedAt = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    public async Task ReconcileStatusAsync(Driver driver, Guid? excludedTripId = null)
    {
        var currentStatus = driver.Status?.Trim().ToUpperInvariant();
        if (currentStatus is "DELETED" or "INACTIVE" or "SUSPENDED_DOCS"
            or "PLANNING" or "ONTRIP" or "ON_TRIP")
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (dayHours, weekHours) = await SumHoursAsync(driver.DriverId, today, excludedTripId);

        var overLimit = dayHours >= MaxDailyHours || weekHours >= MaxWeeklyHours;

        if (overLimit)
        {
            driver.Status = StatusRelax;
        }
        else if (driver.Status == StatusRelax)
        {
            // The restriction window has rolled over — driver may be assigned again.
            driver.Status = StatusAvailable;
        }
    }

    /// <summary>Sum a driver's logged hours for the calendar day and the Mon–Sun week containing <paramref name="day"/>.</summary>
    private async Task<(decimal dayHours, decimal weekHours)> SumHoursAsync(
        Guid driverId,
        DateOnly day,
        Guid? excludedTripId = null)
    {
        var (weekStart, weekEnd) = CalendarWeek(day);

        var logs = await _context.DriverWorkLogs
            .Where(w => w.DriverId == driverId
                && w.WorkDate >= weekStart
                && w.WorkDate <= weekEnd
                && (!excludedTripId.HasValue || w.TripId != excludedTripId.Value))
            .Select(w => new { w.WorkDate, w.DrivingHours })
            .ToListAsync();

        var dayHours = 0m;
        var weekHours = 0m;
        foreach (var log in logs)
        {
            weekHours += log.DrivingHours;
            if (log.WorkDate == day) dayHours += log.DrivingHours;
        }

        return (dayHours, weekHours);
    }

    /// <summary>Monday-to-Sunday calendar week containing <paramref name="day"/>.</summary>
    private static (DateOnly start, DateOnly end) CalendarWeek(DateOnly day)
    {
        // DayOfWeek: Sunday=0 ... Saturday=6. We want Monday as the first day.
        int daysFromMonday = ((int)day.DayOfWeek + 6) % 7;
        var start = day.AddDays(-daysFromMonday);
        return (start, start.AddDays(6));
    }
}
