using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class AllocationResultResponse
    {
        public Guid ReferenceDocumentId { get; set; }
        public List<AllocatedItemDetailDto> Items { get; set; } = new();
    }

    public class AllocatedItemDetailDto
    {
        public string ItemCode { get; set; } = null!;
        public decimal RequestedQuantity { get; set; }
        public List<AllocatedBatchDetailDto> Allocations { get; set; } = new();
    }

    public class AllocatedBatchDetailDto
    {
        public Guid StockId { get; set; }
        public Guid BatchId { get; set; }
        public string BatchNumber { get; set; } = null!;
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = null!;
        public decimal AllocatedQuantity { get; set; }
    }
}
