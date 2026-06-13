using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class UpdateOutboundOrderRequest
    {
        public string ReceiverName { get; set; } = null!;
        public string ReceiverPhone { get; set; } = null!;
        public string DestinationAddress { get; set; } = null!;
        public List<CreateOutboundOrderItemRequest> Items { get; set; } = new();
    }
}
