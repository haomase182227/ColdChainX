using System;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public class DriverAvailabilityService : IDriverAvailabilityService
{
    private readonly ApplicationDbContext _context;

    public const decimal MaxDailyHours = 10m;
    public const decimal MaxWeeklyHours = 48m;
    public static readonly TimeSpan RecoveryPeriod = TimeSpan.FromHours(4);

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
            result.Reason = $"Cảnh báo giờ lái dự kiến trong ngày: {dayHours:F1}h + {additionalHours:F1}h > {MaxDailyHours:F0}h. Tài xế cần luân phiên và nghỉ trong chuyến.";
        }
        else if (weekHours + additionalHours > MaxWeeklyHours)
        {
            result.Reason = $"Cảnh báo giờ lái dự kiến trong tuần: {weekHours:F1}h + {additionalHours:F1}h > {MaxWeeklyHours:F0}h. Tài xế cần luân phiên và nghỉ trong chuyến.";
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

        var lastCompletedAt = await _context.TripDrivers
            .Where(td => td.DriverId == driver.DriverId
                && td.Trip.CompletedAt.HasValue
                && (!excludedTripId.HasValue || td.TripId != excludedTripId.Value))
            .MaxAsync(td => td.Trip.CompletedAt);

        if (lastCompletedAt.HasValue
            && DateTime.UtcNow < lastCompletedAt.Value.Add(RecoveryPeriod))
        {
            driver.Status = StatusRelax;
        }
        else if (driver.Status == StatusRelax)
        {
            driver.Status = StatusAvailable;
        }
    }

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

    private static (DateOnly start, DateOnly end) CalendarWeek(DateOnly day)
    {
        int daysFromMonday = ((int)day.DayOfWeek + 6) % 7;
        var start = day.AddDays(-daysFromMonday);
        return (start, start.AddDays(6));
    }
}
