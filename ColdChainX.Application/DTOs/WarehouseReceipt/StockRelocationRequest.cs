using System;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    public class StockRelocationRequest
    {
        public Guid SourceLocationId { get; set; }
        public Guid DestinationLocationId { get; set; }
        public string ItemCode { get; set; } = null!;
        public Guid BatchId { get; set; }
        public decimal Quantity { get; set; }
        public int Pallets { get; set; } // Pallets to occupy in destination and free from source
    }
}
