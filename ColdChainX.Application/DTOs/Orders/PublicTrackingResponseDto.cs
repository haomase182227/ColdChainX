using System;

namespace ColdChainX.Application.DTOs.Orders;

public class PublicTrackingResponseDto
{
    public string TrackingCode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string DeliveryAddress { get; set; } = null!;
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public decimal? CurrentTemperature { get; set; }
    public double? RemainingDistanceKm { get; set; }
    public int? EstimatedMinutesToArrival { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}