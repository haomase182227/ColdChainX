using System;

namespace ColdChainX.Application.DTOs.Orders;

public class TrackingPointDto
{
    public DateTime Timestamp { get; set; }
    public decimal TempC { get; set; }
    public decimal Lat { get; set; }
    public decimal Lon { get; set; }
}