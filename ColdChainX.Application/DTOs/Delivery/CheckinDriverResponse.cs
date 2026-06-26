using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class CheckinDriverResponse
{
    public Guid TripId { get; set; }
    public Guid StopId { get; set; }
    public string Address { get; set; } = null!;
    public double DistanceInMeters { get; set; }
    public DateTime CheckedinAt { get; set; }
}
