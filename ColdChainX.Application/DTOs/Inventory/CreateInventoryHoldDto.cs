using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload to create a lock/hold on stock (quarantine, QA inspection).
    /// </summary>
    public class CreateInventoryHoldDto
    {
        /// <summary>
        /// Unique system identifier of the stock record.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Quantity to place on hold.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Reason code explaining why the stock is held (e.g. TEMP_DEVIATION, DAMAGED, QA_PENDING).
        /// </summary>
        public string ReasonCode { get; set; } = null!;

        /// <summary>
        /// Optional detail notes.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Target quarantine location identifier (required if holding a partial quantity to separate physical stock).
        /// </summary>
        public Guid? TargetQuarantineLocationId { get; set; }
    }
}
