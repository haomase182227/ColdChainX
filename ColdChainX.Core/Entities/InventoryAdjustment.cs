using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities
{
    public partial class InventoryAdjustment
    {
        public Guid AdjustmentId { get; set; }
        public Guid StockId { get; set; }
        public InventoryAdjustmentType AdjustmentType { get; set; }
        public decimal QuantityBefore { get; set; }
        public decimal QuantityChanged { get; set; }
        public decimal QuantityAfter { get; set; }
        public string ReasonNotes { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid MovementId { get; set; }

        public virtual InventoryStock Stock { get; set; } = null!;
        public virtual InventoryMovement Movement { get; set; } = null!;
    }
}
