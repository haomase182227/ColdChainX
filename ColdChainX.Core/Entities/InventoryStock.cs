using System;

namespace ColdChainX.Core.Entities;

public partial class InventoryStock
{
    public Guid StockId { get; set; }
    public Guid LocationId { get; set; }
    public string ItemCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string Unit { get; set; } = null!;
    public Guid BatchId { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal QuantityAllocated { get; set; }
    public DateTime InboundDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public int PalletCount { get; set; }
    public decimal? RequiredTempMin { get; set; }
    public decimal? RequiredTempMax { get; set; }

    public virtual WarehouseLocation Location { get; set; } = null!;
    public virtual InventoryBatch Batch { get; set; } = null!;
}
