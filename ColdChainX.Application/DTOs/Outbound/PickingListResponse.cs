using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Response model representing the picking list for an outbound order.
    /// </summary>
    public class PickingListResponse
    {
        /// <summary>
        /// Unique system identifier of the outbound order.
        /// </summary>
        public Guid OutboundOrderId { get; set; }

        /// <summary>
        /// Unique business code for the outbound order.
        /// </summary>
        public string OrderCode { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the picker assigned to this order.
        /// </summary>
        public Guid? AssignedPickerId { get; set; }

        /// <summary>
        /// Username of the picker assigned to this order.
        /// </summary>
        public string? AssignedPickerName { get; set; }

        /// <summary>
        /// List of picking instructions per stock location/batch.
        /// </summary>
        public List<PickingListItemDto> Items { get; set; } = new();
    }
}
