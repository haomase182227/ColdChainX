using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class CreateOutboundOrderRequest
    {
        public Guid CustomerId { get; set; }
        public string ReceiverName { get; set; } = null!;
        public string ReceiverPhone { get; set; } = null!;
        public string DestinationAddress { get; set; } = null!;
        public List<CreateOutboundOrderItemRequest> Items { get; set; } = new();
    }

    public class CreateOutboundOrderItemRequest
    {
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal Quantity { get; set; }
    }
}
