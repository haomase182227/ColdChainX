using System;

namespace ColdChainX.Application.DTOs.Fleet;

public class DriverTripHistoryResponseDto
{
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = null!;
    public string? Status { get; set; }
    public string? VehiclePlate { get; set; }
    public string? RouteName { get; set; }
    public string Origin { get; set; } = null!;
    public string Destination { get; set; } = null!;
    public DateTime PlannedStartTime { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string DriverRole { get; set; } = null!;
    public int TotalOrders { get; set; }
    public decimal WorkHours { get; set; }
    public decimal? DistanceKm { get; set; }
}