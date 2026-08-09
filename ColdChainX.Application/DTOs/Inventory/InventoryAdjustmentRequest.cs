using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class InventoryAdjustmentRequest
    {
        public Guid StockId { get; set; }

        public InventoryAdjustmentType AdjustmentType { get; set; }
        
        public bool IsAbsoluteCount { get; set; }

        public decimal Quantity { get; set; }

        public int Pallets { get; set; }
        
        public string Reason { get; set; } = null!;
    }
}
