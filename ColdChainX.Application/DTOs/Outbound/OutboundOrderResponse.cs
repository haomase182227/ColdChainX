using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    /// <summary>
    /// Response model representing outbound order properties and lines.
    /// </summary>
    public class OutboundOrderResponse
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
        /// Unique identifier of the customer.
        /// </summary>
        public Guid CustomerId { get; set; }

        /// <summary>
        /// Name of the customer.
        /// </summary>
        public string CustomerName { get; set; } = null!;

        /// <summary>
        /// Name of the cargo receiver.
        /// </summary>
        public string ReceiverName { get; set; } = null!;

        /// <summary>
        /// Contact phone number of the receiver.
        /// </summary>
        public string ReceiverPhone { get; set; } = null!;

        /// <summary>
        /// Target destination address.
        /// </summary>
        public string DestinationAddress { get; set; } = null!;

        /// <summary>
        /// Operational status of the order (e.g., DRAFT, ALLOCATED, PICKING, COMPLETED, CANCELLED).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Unique identifier of the assigned picker operator.
        /// </summary>
        public Guid? AssignedPickerId { get; set; }

        /// <summary>
        /// Name of the assigned picker.
        /// </summary>
        public string? AssignedPickerName { get; set; }

        /// <summary>
        /// Timestamp when stock was allocated.
        /// </summary>
        public DateTime? AllocatedAt { get; set; }

        /// <summary>
        /// Timestamp when the order was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Line items in the outbound order.
        /// </summary>
        public List<OutboundOrderItemResponse> Items { get; set; } = new();
    }

    /// <summary>
    /// Line item details in the outbound order response.
    /// </summary>
    public class OutboundOrderItemResponse
    {
        /// <summary>
        /// Unique system identifier of the order line item.
        /// </summary>
        public Guid OutboundOrderItemId { get; set; }

        /// <summary>
        /// Unique product code.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Product display name.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Unit of measure.
        /// </summary>
        public string Unit { get; set; } = null!;

        /// <summary>
        /// Quantity of product requested.
        /// </summary>
        public decimal Quantity { get; set; }
    }
}
