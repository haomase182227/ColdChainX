using System;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

public interface IDriverAvailabilityService
{
    Task<DriverAvailability> CheckAsync(Guid driverId, decimal additionalHours, DateOnly day);

    Task RecordWorkAsync(Guid driverId, Guid tripId, decimal hours, DateOnly day);

    Task ReconcileStatusAsync(Driver driver, Guid? excludedTripId = null);
}

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
