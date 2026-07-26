using System;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

/// <summary>
/// Enforces legal driving-hour limits (10h/day, 48h/week) using calendar windows
/// (day = UTC midnight, week = Mon–Sun). Drivers exceeding a limit are placed in
/// the RELAX status and cannot be assigned until the window rolls over.
/// </summary>
public interface IDriverAvailabilityService
{
    /// <summary>
    /// Check whether <paramref name="additionalHours"/> can be added to the driver's
    /// schedule on <paramref name="day"/> without breaching the daily or weekly limit.
    /// </summary>
    Task<DriverAvailability> CheckAsync(Guid driverId, decimal additionalHours, DateOnly day);

    /// <summary>Insert a work-hour record for the given driver/trip/day.</summary>
    Task RecordWorkAsync(Guid driverId, Guid tripId, decimal hours, DateOnly day);

    /// <summary>
    /// Recompute the driver's RELAX state against the current calendar day/week and
    /// adjust <see cref="Driver.Status"/> in place (RELAX ⇄ Available) so the
    /// restriction auto-clears once the window expires. Does not call SaveChanges.
    /// </summary>
    Task ReconcileStatusAsync(Driver driver, Guid? excludedTripId = null);
}

/// <summary>Outcome of a driving-hour availability check.</summary>
public class DriverAvailability
{
    public Guid DriverId { get; set; }
    public bool CanAssign { get; set; }
    public decimal DayHours { get; set; }
    public decimal WeekHours { get; set; }
    public decimal MaxDailyHours { get; set; }
    public decimal MaxWeeklyHours { get; set; }
    public string? Reason { get; set; }
}
