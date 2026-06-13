using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class InventoryAdjustmentRequest
    {
        public Guid StockId { get; set; }
        public InventoryAdjustmentType AdjustmentType { get; set; }
        
        // Mode flag to determine how to apply the quantity and pallets
        public bool IsAbsoluteCount { get; set; } // If true: quantity and pallets are absolute counts. If false: they are deltas.
        public decimal Quantity { get; set; } // delta change or absolute count
        public int Pallets { get; set; } // delta change or absolute count
        
        public string Reason { get; set; } = null!;
    }
}
