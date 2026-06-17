using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Request payload for updating an existing outbound order.
    /// </summary>
    public class UpdateOutboundOrderRequest
    {
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
        /// Updated list of requested products and quantities.
        /// </summary>
        public List<CreateOutboundOrderItemRequest> Items { get; set; } = new();
    }
}
