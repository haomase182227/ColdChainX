using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing the outcome of an inventory allocation request.
    /// </summary>
    public class AllocationResultResponse
    {
        /// <summary>
        /// Unique identifier of the associated reference document.
        /// </summary>
        public Guid ReferenceDocumentId { get; set; }

        /// <summary>
        /// Detailed results for each requested item.
        /// </summary>
        public List<AllocatedItemDetailDto> Items { get; set; } = new();
    }

    /// <summary>
    /// Outcome detail for a requested item allocation.
    /// </summary>
    public class AllocatedItemDetailDto
    {
        /// <summary>
        /// Unique product code.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Original quantity requested.
        /// </summary>
        public decimal RequestedQuantity { get; set; }

        /// <summary>
        /// Allocated stock locations and batches.
        /// </summary>
        public List<AllocatedBatchDetailDto> Allocations { get; set; } = new();
    }

    /// <summary>
    /// Detail of a single stock allocation chunk (location and batch).
    /// </summary>
    public class AllocatedBatchDetailDto
    {
        /// <summary>
        /// Unique system identifier of the stock record.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Unique identifier of the batch.
        /// </summary>
        public Guid BatchId { get; set; }

        /// <summary>
        /// Batch number string.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the allocated warehouse location.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Code of the allocated location.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Allocated quantity from this location/batch.
        /// </summary>
        public decimal AllocatedQuantity { get; set; }
    }
}
