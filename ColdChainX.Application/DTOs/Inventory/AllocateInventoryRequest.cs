using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class AllocateInventoryRequest
    {
        public Guid ReferenceDocumentId { get; set; }
        public List<AllocateInventoryItemRequest> Items { get; set; } = new();
    }

    public class AllocateInventoryItemRequest
    {
        public string ItemCode { get; set; } = null!;
        public decimal Quantity { get; set; }
    }
}
