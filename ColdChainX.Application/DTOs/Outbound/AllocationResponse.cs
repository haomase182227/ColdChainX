using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class AllocationResponse
    {
        public Guid OutboundOrderId { get; set; }
        public string OrderCode { get; set; } = null!;
        public List<AllocationItemDto> Allocations { get; set; } = new();
    }
}
