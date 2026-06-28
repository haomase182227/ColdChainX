using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Delivery;

public class TripDeliveryProgressResponse
{
    public Guid TripId { get; set; }
    public int TotalLpns { get; set; }
    public int DeliveredCount { get; set; }
    public int RejectedCount { get; set; }
    public int PendingCount { get; set; }
    public bool IsComplete { get; set; }
    public List<LpnDeliveryStatusResponse> LpnStatuses { get; set; } = new();
}
