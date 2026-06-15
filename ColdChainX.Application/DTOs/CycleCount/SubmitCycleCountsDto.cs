using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.CycleCount
{
    public class SubmitCycleCountsDto
    {
        public List<SubmitEntryCountDto> Counts { get; set; } = new();
    }

    public class SubmitEntryCountDto
    {
        public Guid EntryId { get; set; }
        public decimal CountedQuantity { get; set; }
        public int CountedPallets { get; set; }
        public string? FoundItemCode { get; set; }
        public Guid? FoundBatchId { get; set; }
    }
}
