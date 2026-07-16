using System;

namespace ColdChainX.Application.DTOs.Dispatch;

public class FilterLpnResponse
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ActualCbm { get; set; }
    public string Category { get; set; } = null!;
    public decimal? RequiredTemperature { get; set; }
    public bool HasStrongOdor { get; set; }
    public string? SlaDeadline { get; set; }
    public string? DepartureTime { get; set; }
    public Guid? OrderId { get; set; }
    public string? TrackingCode { get; set; }
    public string? ItemName { get; set; }
    public string? CustomerName { get; set; }
    public string? RouteName { get; set; }
    public string State { get; set; } = null!;
    public bool IsCompatible { get; set; }
}
