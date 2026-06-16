using System;

namespace ColdChainX.Core.Entities;

public partial class InventoryMovement
{
    public Guid MovementId { get; set; }
    public Guid? StockId { get; set; }
    public Guid? WarehouseReceiptItemId { get; set; }
    public string ItemCode { get; set; } = null!;
    public Guid BatchId { get; set; }
    public string MovementType { get; set; } = null!; // INBOUND, OUTBOUND, PUTAWAY, RELOCATION, ADJUSTMENT
    public decimal Quantity { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid? ToLocationId { get; set; }
    public Guid? ReferenceDocumentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }

    public virtual InventoryBatch Batch { get; set; } = null!;
    public virtual WarehouseLocation? FromLocation { get; set; }
    public virtual WarehouseLocation? ToLocation { get; set; }
    public virtual WarehouseReceiptItem? WarehouseReceiptItem { get; set; }
}
