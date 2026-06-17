using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.CycleCount
{
    /// <summary>
    /// Request payload to submit physical counts for entries in a cycle count plan.
    /// </summary>
    public class SubmitCycleCountsDto
    {
        /// <summary>
        /// List of submitted location counts.
        /// </summary>
        public List<SubmitEntryCountDto> Counts { get; set; } = new();
    }

    /// <summary>
    /// Physical count submission details for a specific location entry.
    /// </summary>
    public class SubmitEntryCountDto
    {
        /// <summary>
        /// Unique system identifier of the cycle count entry.
        /// </summary>
        public Guid EntryId { get; set; }

        /// <summary>
        /// Physically verified product quantity at this location.
        /// </summary>
        public decimal CountedQuantity { get; set; }

        /// <summary>
        /// Physically verified pallet count at this location.
        /// </summary>
        public int CountedPallets { get; set; }

        /// <summary>
        /// Optional product code (used if an unexpected/found item is stored here).
        /// </summary>
        public string? FoundItemCode { get; set; }

        /// <summary>
        /// Optional batch ID (used if an unexpected/found batch is stored here).
        /// </summary>
        public Guid? FoundBatchId { get; set; }
    }
}
