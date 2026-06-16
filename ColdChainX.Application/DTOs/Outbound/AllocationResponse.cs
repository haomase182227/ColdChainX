using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Response model representing all stock allocations for an outbound order.
    /// </summary>
    public class AllocationResponse
    {
        /// <summary>
        /// Unique system identifier of the outbound order.
        /// </summary>
        public Guid OutboundOrderId { get; set; }

        /// <summary>
        /// Code identifying the outbound order.
        /// </summary>
        public string OrderCode { get; set; } = null!;

        /// <summary>
        /// List of allocated stock chunks.
        /// </summary>
        public List<AllocationItemDto> Allocations { get; set; } = new();
    }
}
