using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class CheckinDriverResponse
{
    public Guid StopId { get; set; }
    public DateTime CheckinTime { get; set; }
    public string ProofImageUrl { get; set; } = string.Empty;
    public double DistanceMeters { get; set; }
    public string Status { get; set; } = "ARRIVED";
}
