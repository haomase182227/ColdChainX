using System;

namespace ColdChainX.Core.Entities
{
    public partial class InventoryAllocation
    {
        public Guid AllocationId { get; set; }
        public Guid ReferenceDocumentId { get; set; }
        public Guid StockId { get; set; }
        public decimal AllocatedQuantity { get; set; }
        public string Status { get; set; } = null!; // ALLOCATED, COMPLETED, RELEASED
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }

        public virtual InventoryStock Stock { get; set; } = null!;
    }
}
