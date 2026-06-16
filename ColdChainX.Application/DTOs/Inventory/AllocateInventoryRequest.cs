using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload to allocate stock items to a document (e.g., outbound shipment, order).
    /// </summary>
    public class AllocateInventoryRequest
    {
        /// <summary>
        /// Unique identifier of the associated reference document (e.g., outbound transport order).
        /// </summary>
        public Guid ReferenceDocumentId { get; set; }

        /// <summary>
        /// List of items to allocate.
        /// </summary>
        public List<AllocateInventoryItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Details of a specific item requested for allocation.
    /// </summary>
    public class AllocateInventoryItemRequest
    {
        /// <summary>
        /// Unique code identifying the product.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Requested quantity to allocate.
        /// </summary>
        public decimal Quantity { get; set; }
    }
}
