using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Inventory.DTOs;

public class LpnDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string? BatchNumber { get; set; }
    public Guid? ReceiptId { get; set; }
    public bool HasWarehouseReceipt { get; set; }
    public string? WarehouseReceiptPdfUrl { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? StorageLocation { get; set; }
    public int Quantity { get; set; }
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public string State { get; set; } = null!;
    public string? Condition { get; set; }
    public DateTime? InboundTime { get; set; }
    public DateTime? SlaDeadline { get; set; }
}
