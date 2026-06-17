using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Request payload to release active stock allocations.
    /// </summary>
    public class ReleaseAllocationRequest
    {
        /// <summary>
        /// Unique identifier of the associated reference document (e.g. outbound transport order) to free stock from.
        /// </summary>
        public Guid ReferenceDocumentId { get; set; }
    }
}
