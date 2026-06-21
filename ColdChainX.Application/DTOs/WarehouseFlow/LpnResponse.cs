using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class LpnResponse
{
    public Guid LpnId { get; set; }

    public string LpnCode { get; set; } = null!;

    public Guid OrderId { get; set; }

    public string? TrackingCode { get; set; }

    public string ItemName { get; set; } = null!;

    public string? StorageLocation { get; set; }

    public int Quantity { get; set; }

    public decimal ExpectedWeightKg { get; set; }

    public decimal ActualWeightKg { get; set; }

    public decimal ExpectedCbm { get; set; }

    public decimal ActualCbm { get; set; }

    public decimal MaxDiffPercent { get; set; }

    public LpnState State { get; set; }

    public string? DiscrepancyReason { get; set; }

    public string? GrnPdfUrl { get; set; }

    public string? DiscrepancyPdfUrl { get; set; }

    public string? ReturnPdfUrl { get; set; }

    public DateTime? InboundTime { get; set; }

    public DateTime? SlaDeadline { get; set; }

    public string? AgingColor { get; set; }
}
