using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Outbound
{
    public class PickingListResponse
    {
        public Guid OutboundOrderId { get; set; }
        public string OrderCode { get; set; } = null!;
        public Guid? AssignedPickerId { get; set; }
        public string? AssignedPickerName { get; set; }
        public List<PickingListItemDto> Items { get; set; } = new();
    }
}
