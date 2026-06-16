using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing available stock in warehouse locations.
    /// </summary>
    public class AvailableStockResponse
    {
        /// <summary>
        /// Unique system identifier of the stock record.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Unique code of the product.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Display name of the product.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Unit of measure.
        /// </summary>
        public string Unit { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the storage location.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Location code where the stock resides.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the inventory batch.
        /// </summary>
        public Guid BatchId { get; set; }

        /// <summary>
        /// Batch/Lot number.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Expiration date of the batch.
        /// </summary>
        public DateOnly ExpiryDate { get; set; }

        /// <summary>
        /// Timestamp when the stock arrived/was received.
        /// </summary>
        public DateTime InboundDate { get; set; }

        /// <summary>
        /// Total quantity currently on hand in the location.
        /// </summary>
        public decimal QuantityOnHand { get; set; }

        /// <summary>
        /// Quantity allocated to outbound orders (locked).
        /// </summary>
        public decimal QuantityAllocated { get; set; }

        /// <summary>
        /// Quantity available to allocate or pick (QuantityOnHand - QuantityAllocated).
        /// </summary>
        public decimal QuantityAvailable => QuantityOnHand - QuantityAllocated;
    }
}
