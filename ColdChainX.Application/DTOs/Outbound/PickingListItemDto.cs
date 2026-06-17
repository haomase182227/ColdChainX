using System;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Represents a single line item in a warehouse picking list.
    /// </summary>
    public class PickingListItemDto
    {
        /// <summary>
        /// Unique code of the product to pick.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Display name of the product.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the source storage location to pick from.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Code of the source location.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Code of the zone where the source location resides.
        /// </summary>
        public string ZoneCode { get; set; } = null!;

        /// <summary>
        /// Lot/Batch number of the stock to pick.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Expiration date of the batch (FEFO sorted).
        /// </summary>
        public DateOnly ExpiryDate { get; set; }

        /// <summary>
        /// Total quantity to pick from this location/batch.
        /// </summary>
        public decimal QuantityToPick { get; set; }
    }
}
