using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model for inventory stocks that have been stored for too long (aging inventory).
    /// </summary>
    public class AgingStockResponse
    {
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public DateTime InboundDate { get; set; }
        public int StorageDays { get; set; }
        public decimal QuantityOnHand { get; set; }
        public int PalletCount { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
    }
}
