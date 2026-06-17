using System;

namespace ColdChainX.Core.Entities;

public partial class InventoryHold
{
    public Guid HoldId { get; set; }
    public Guid StockId { get; set; }
    public decimal HoldQuantity { get; set; }
    public string ReasonCode { get; set; } = null!;
    public string? Notes { get; set; }
    public string Status { get; set; } = "HOLD"; // HOLD, RELEASED, ADJUSTED
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public Guid? ReleasedBy { get; set; }
    public string? ReleaseNotes { get; set; }
    public Guid? AdjustmentId { get; set; }

    public virtual InventoryStock Stock { get; set; } = null!;
    public virtual User Creator { get; set; } = null!;
    public virtual User? Releaser { get; set; }
    public virtual InventoryAdjustment? Adjustment { get; set; }
}
