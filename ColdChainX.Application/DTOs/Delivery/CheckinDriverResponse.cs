using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class CheckinDriverResponse
{
    public Guid StopId { get; set; }
    public DateTime CheckinTime { get; set; }
    public string? RemovedSealCode { get; set; }
    public System.Collections.Generic.List<LpnUnloadInfo> LpnsToUnload { get; set; } = new();
}
