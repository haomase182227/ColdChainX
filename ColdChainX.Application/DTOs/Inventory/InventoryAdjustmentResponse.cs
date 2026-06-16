using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing a recorded stock adjustment request.
    /// </summary>
    public class InventoryAdjustmentResponse
    {
        /// <summary>
        /// Unique system identifier of the adjustment record.
        /// </summary>
        public Guid AdjustmentId { get; set; }

        /// <summary>
        /// Unique system identifier of the stock record.
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
        /// Lot/Batch number.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Code of the storage location.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Type of physical adjustment (e.g. Damage).
        /// </summary>
        public InventoryAdjustmentType AdjustmentType { get; set; }

        /// <summary>
        /// Quantity before the adjustment was applied.
        /// </summary>
        public decimal QuantityBefore { get; set; }

        /// <summary>
        /// Quantity variation applied.
        /// </summary>
        public decimal QuantityChanged { get; set; }

        /// <summary>
        /// Quantity after the adjustment was applied.
        /// </summary>
        public decimal QuantityAfter { get; set; }

        /// <summary>
        /// Pallets count before the adjustment.
        /// </summary>
        public int PalletsBefore { get; set; }

        /// <summary>
        /// Pallets variation applied.
        /// </summary>
        public int PalletsChanged { get; set; }

        /// <summary>
        /// Pallets count after adjustment.
        /// </summary>
        public int PalletsAfter { get; set; }

        /// <summary>
        /// Notes explaining the adjustment details or reasons.
        /// </summary>
        public string ReasonNotes { get; set; } = null!;

        /// <summary>
        /// Approval status (e.g., PENDING_APPROVAL, APPROVED, REJECTED).
        /// </summary>
        public InventoryAdjustmentStatus Status { get; set; }

        /// <summary>
        /// Unique identifier of the associated stock movement (if approved).
        /// </summary>
        public Guid? MovementId { get; set; }

        /// <summary>
        /// Timestamp when the adjustment request was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who requested the adjustment.
        /// </summary>
        public Guid CreatedBy { get; set; }

        /// <summary>
        /// Username of the user who requested the adjustment.
        /// </summary>
        public string CreatedByUsername { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the manager who approved the adjustment.
        /// </summary>
        public Guid? ApprovedBy { get; set; }

        /// <summary>
        /// Username of the manager who approved the adjustment.
        /// </summary>
        public string? ApprovedByUsername { get; set; }

        /// <summary>
        /// Timestamp when the adjustment was approved.
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Explanation details if the adjustment request was rejected.
        /// </summary>
        public string? RejectionReason { get; set; }
    }
}
