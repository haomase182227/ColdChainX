using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class CreateInventoryHoldDto
    {
        public Guid StockId { get; set; }
        public decimal Quantity { get; set; }
        public string ReasonCode { get; set; } = null!;
        public string? Notes { get; set; }
        public Guid? TargetQuarantineLocationId { get; set; } // Required if holding partial quantity
    }
}
