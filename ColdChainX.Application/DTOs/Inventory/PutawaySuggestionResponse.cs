using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    public class PutawaySuggestionResponse
    {
        public Guid LocationId { get; set; }
        public string LocationCode { get; set; } = null!;
        public string ZoneCode { get; set; } = null!;
        public int CurrentPallets { get; set; }
        public int MaxCapacityPallets { get; set; }
        public int RemainingCapacity { get; set; }
        public int SuitabilityScore { get; set; }
        public string MatchType { get; set; } = null!; // "SAME_BATCH", "SAME_ITEM", "EMPTY", "COMPATIBLE"
    }

    public class StockPutawaySuggestionsResponse
    {
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string BatchNumber { get; set; } = null!;
        public int PalletCount { get; set; }
        public List<PutawaySuggestionResponse> Suggestions { get; set; } = new();
    }
}
