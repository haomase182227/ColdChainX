using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities
{
    public class CycleCountEntry
    {
        public Guid EntryId { get; set; }
        public Guid PlanId { get; set; }
        public Guid LocationId { get; set; }
        public Guid? StockId { get; set; }
        
        // Audit Snapshot fields / Support unexpected stock finds
        public string ItemCode { get; set; } = null!;
        public Guid? BatchId { get; set; }
        
        public decimal SystemQuantity { get; set; }
        public int SystemPallets { get; set; }
        
        public decimal? CountedQuantity { get; set; }
        public int? CountedPallets { get; set; }
        
        public decimal? VarianceQuantity { get; set; }
        public int? VariancePallets { get; set; }
        
        public CycleCountEntryStatus Status { get; set; } = CycleCountEntryStatus.PENDING;
        public DateTime? CountedAt { get; set; }
        public Guid? CountedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public Guid? ReviewedBy { get; set; }
        public string? ManagerNotes { get; set; }
        public Guid? AdjustmentId { get; set; }

        public virtual CycleCountPlan Plan { get; set; } = null!;
        public virtual WarehouseLocation Location { get; set; } = null!;
        public virtual InventoryStock? Stock { get; set; }
        public virtual InventoryBatch? Batch { get; set; }
        public virtual User? Counter { get; set; }
        public virtual User? Reviewer { get; set; }
        public virtual InventoryAdjustment? Adjustment { get; set; }
    }
}
