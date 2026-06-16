using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload to adjust inventory quantities (cycle counts, damages, losses, expirations, quality holds).
    /// </summary>
    public class InventoryAdjustmentRequest
    {
        /// <summary>
        /// Unique system identifier of the stock record.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Type of physical adjustment (e.g., CycleCount, Damage, Loss, Expiration, QualityHold, InboundQC).
        /// </summary>
        public InventoryAdjustmentType AdjustmentType { get; set; }
        
        /// <summary>
        /// Mode flag to determine how to apply quantities:
        /// - If true, Quantity and Pallets are absolute counts.
        /// - If false, they are relative delta changes (+/- deltas).
        /// </summary>
        public bool IsAbsoluteCount { get; set; }

        /// <summary>
        /// Adjustment quantity (either relative delta or absolute count depending on IsAbsoluteCount).
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Adjustment pallets (either relative delta or absolute count depending on IsAbsoluteCount).
        /// </summary>
        public int Pallets { get; set; }
        
        /// <summary>
        /// Detailed description or reason for the adjustment.
        /// </summary>
        public string Reason { get; set; } = null!;
    }
}
