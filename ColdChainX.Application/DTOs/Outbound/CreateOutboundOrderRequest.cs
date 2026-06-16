using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Request payload for creating a new outbound order.
    /// </summary>
    public class CreateOutboundOrderRequest
    {
        /// <summary>
        /// Unique system identifier of the ordering customer.
        /// </summary>
        public Guid CustomerId { get; set; }

        /// <summary>
        /// Name of the cargo receiver.
        /// </summary>
        public string ReceiverName { get; set; } = null!;

        /// <summary>
        /// Contact phone number of the receiver.
        /// </summary>
        public string ReceiverPhone { get; set; } = null!;

        /// <summary>
        /// Target destination delivery address.
        /// </summary>
        public string DestinationAddress { get; set; } = null!;

        /// <summary>
        /// List of requested products and quantities.
        /// </summary>
        public List<CreateOutboundOrderItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Detailed request for a single outbound order line item.
    /// </summary>
    public class CreateOutboundOrderItemRequest
    {
        /// <summary>
        /// Unique product code.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Display name of the product.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Unit of measure (e.g. BOX, PALLET).
        /// </summary>
        public string Unit { get; set; } = null!;

        /// <summary>
        /// Quantity requested for dispatch.
        /// </summary>
        public decimal Quantity { get; set; }
    }
}
