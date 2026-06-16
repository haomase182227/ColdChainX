using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.CycleCount
{
    /// <summary>
    /// Response model representing a line item entry within a cycle count plan.
    /// </summary>
    public class CycleCountEntryResponse
    {
        /// <summary>
        /// Unique system identifier of the cycle count entry.
        /// </summary>
        public Guid EntryId { get; set; }

        /// <summary>
        /// Unique identifier of the parent cycle count plan.
        /// </summary>
        public Guid PlanId { get; set; }

        /// <summary>
        /// Unique identifier of the target storage location to check.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Code of the target storage location.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the expected stock record (null if location is expected to be empty).
        /// </summary>
        public Guid? StockId { get; set; }

        /// <summary>
        /// Expected item code.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the expected batch.
        /// </summary>
        public Guid? BatchId { get; set; }

        /// <summary>
        /// Expected batch/lot number.
        /// </summary>
        public string? BatchNumber { get; set; }
        
        /// <summary>
        /// Stock quantity recorded by the system before counting.
        /// </summary>
        public decimal? SystemQuantity { get; set; }

        /// <summary>
        /// Pallets recorded by the system before counting.
        /// </summary>
        public int? SystemPallets { get; set; }
        
        /// <summary>
        /// Quantity physically counted by the operator.
        /// </summary>
        public decimal? CountedQuantity { get; set; }

        /// <summary>
        /// Pallets physically counted by the operator.
        /// </summary>
        public int? CountedPallets { get; set; }
        
        /// <summary>
        /// Discrepancy quantity (CountedQuantity - SystemQuantity).
        /// </summary>
        public decimal? VarianceQuantity { get; set; }

        /// <summary>
        /// Discrepancy pallets (CountedPallets - SystemPallets).
        /// </summary>
        public int? VariancePallets { get; set; }
        
        /// <summary>
        /// Counting status of this entry (e.g. PENDING, COUNTED, RECONCILED).
        /// </summary>
        public CycleCountEntryStatus Status { get; set; }

        /// <summary>
        /// Timestamp when the physical count was submitted.
        /// </summary>
        public DateTime? CountedAt { get; set; }

        /// <summary>
        /// Unique identifier of the operator who counted the stock.
        /// </summary>
        public Guid? CountedBy { get; set; }

        /// <summary>
        /// Timestamp when the manager reviewed the variance discrepancy.
        /// </summary>
        public DateTime? ReviewedAt { get; set; }

        /// <summary>
        /// Unique identifier of the manager who reviewed the entry.
        /// </summary>
        public Guid? ReviewedBy { get; set; }

        /// <summary>
        /// Verification or resolution notes added by the manager.
        /// </summary>
        public string? ManagerNotes { get; set; }

        /// <summary>
        /// Unique identifier of the resulting inventory adjustment (if approved with a variance).
        /// </summary>
        public Guid? AdjustmentId { get; set; }
    }
}
