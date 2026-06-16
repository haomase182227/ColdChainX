using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing active or historical inventory hold details.
    /// </summary>
    public class HoldResponseDto
    {
        /// <summary>
        /// Unique system identifier of the inventory hold record.
        /// </summary>
        public Guid HoldId { get; set; }

        /// <summary>
        /// Unique system identifier of the associated stock.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Code of the product.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Display name of the product.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Code of the location where the held stock resides.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Quantity on hold.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Reason code for the hold (e.g. TEMP_DEVIATION).
        /// </summary>
        public string ReasonCode { get; set; } = null!;

        /// <summary>
        /// Status of the hold (e.g., ACTIVE, RELEASED, ADJUSTED_OUT).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Timestamp when the hold was placed.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the operator who placed the hold.
        /// </summary>
        public Guid CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the hold was released.
        /// </summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>
        /// Unique identifier of the supervisor who authorized release.
        /// </summary>
        public Guid? ReleasedBy { get; set; }

        /// <summary>
        /// Notes explaining the release or QA resolution.
        /// </summary>
        public string? ReleaseNotes { get; set; }
    }
}
