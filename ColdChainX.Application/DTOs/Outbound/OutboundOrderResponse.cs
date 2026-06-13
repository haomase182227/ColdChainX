using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class OutboundOrderResponse
    {
        public Guid OutboundOrderId { get; set; }
        public string OrderCode { get; set; } = null!;
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string ReceiverName { get; set; } = null!;
        public string ReceiverPhone { get; set; } = null!;
        public string DestinationAddress { get; set; } = null!;
        public string Status { get; set; } = null!; // String representation of OutboundOrderStatus
        public Guid? AssignedPickerId { get; set; }
        public string? AssignedPickerName { get; set; }
        public DateTime? AllocatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<OutboundOrderItemResponse> Items { get; set; } = new();
    }

    public class OutboundOrderItemResponse
    {
        public Guid OutboundOrderItemId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal Quantity { get; set; }
    }
}
