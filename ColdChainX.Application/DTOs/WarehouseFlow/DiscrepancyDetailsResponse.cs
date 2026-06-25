using System;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class DiscrepancyDetailsResponse
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int ExpectedQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int Quantity { get; set; }
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ExpectedCbm { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal ExpectedLengthCm { get; set; }
    public decimal ActualLengthCm { get; set; }
    public decimal ExpectedWidthCm { get; set; }
    public decimal ActualWidthCm { get; set; }
    public decimal ExpectedHeightCm { get; set; }
    public decimal ActualHeightCm { get; set; }
    public bool IsQuantityDifferent { get; set; }
    public bool IsWeightDifferent { get; set; }
    public bool IsCbmDifferent { get; set; }
    public bool IsLengthDifferent { get; set; }
    public bool IsWidthDifferent { get; set; }
    public bool IsHeightDifferent { get; set; }
    public decimal? RequiredTemperature { get; set; }
    public decimal? RecordedTemperature { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public string? DiscrepancyReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DiscrepancyReceiptInfo ReceiptInfo { get; set; } = null!;
}

public class DiscrepancyReceiptInfo
{
    public Guid ReceiptId { get; set; }
    public string ReceiptCode { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = null!;
    public decimal? RecordedTemperature { get; set; }
    public string DelivererName { get; set; } = null!;
    public string ReceiverName { get; set; } = null!;
    public string? Note { get; set; }
    public string? PdfUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
}
