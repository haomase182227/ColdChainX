using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class AvailableStockResponse
    {
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = null!;
        public Guid BatchId { get; set; }
        public string BatchNumber { get; set; } = null!;
        public DateOnly ExpiryDate { get; set; }
        public DateTime InboundDate { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal QuantityAllocated { get; set; }
        public decimal QuantityAvailable => QuantityOnHand - QuantityAllocated;
    }
}
