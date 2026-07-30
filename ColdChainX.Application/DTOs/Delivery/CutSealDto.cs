using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class CutSealRequest
{
    public Guid? StopId { get; set; }
}

public class CutSealResponse
{
    public Guid SealId { get; set; }
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string SealCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RemovedAt { get; set; }
    public bool AiAlertingMuted { get; set; }
    public string AiMutedReason { get; set; } = string.Empty;
    public int MutedDurationHours { get; set; }
}
