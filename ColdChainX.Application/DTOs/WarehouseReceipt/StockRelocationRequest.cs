using System;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    /// <summary>
    /// Request payload for relocating stock between locations.
    /// </summary>
    public class StockRelocationRequest
    {
        /// <summary>
        /// Source location unique identifier.
        /// </summary>
        public Guid SourceLocationId { get; set; }

        /// <summary>
        /// Destination location unique identifier.
        /// </summary>
        public Guid DestinationLocationId { get; set; }

        /// <summary>
        /// Business code representing the item.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the inventory batch.
        /// </summary>
        public Guid BatchId { get; set; }

        /// <summary>
        /// Quantity of stock unit to move.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Number of pallets to occupy in the destination and release from the source.
        /// </summary>
        public int Pallets { get; set; }
    }
}
