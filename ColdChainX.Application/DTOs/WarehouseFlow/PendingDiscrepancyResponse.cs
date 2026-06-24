using System;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class PendingDiscrepancyResponse
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string? CustomerName { get; set; }
    public string ItemName { get; set; } = null!;
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ExpectedCbm { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal DiffPercent { get; set; }
    public string? DiscrepancyReason { get; set; }
    public Guid? AsnId { get; set; }
    public string? AsnCode { get; set; }
    public Guid ReceiptId { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
