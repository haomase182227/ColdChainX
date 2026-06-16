using System;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Details of a single stock allocation unit for an outbound item.
    /// </summary>
    public class AllocationItemDto
    {
        /// <summary>
        /// Unique system identifier of the allocation record.
        /// </summary>
        public Guid AllocationId { get; set; }

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
        /// Lot/Batch number of the allocated stock.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Expiration date of the batch (sorted for FEFO).
        /// </summary>
        public DateOnly ExpiryDate { get; set; }

        /// <summary>
        /// Code of the warehouse location from which to pick the stock.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Code of the zone where the location resides.
        /// </summary>
        public string ZoneCode { get; set; } = null!;

        /// <summary>
        /// Quantity allocated from this location/batch.
        /// </summary>
        public decimal AllocatedQuantity { get; set; }

        /// <summary>
        /// Quantity remaining available at this location/batch.
        /// </summary>
        public decimal AvailableQuantity { get; set; }

        /// <summary>
        /// Status of the allocation (e.g. ALLOCATED, PICKED).
        /// </summary>
        public string Status { get; set; } = null!;
    }
}
