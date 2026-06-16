using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing a single putaway location suggestion.
    /// </summary>
    public class PutawaySuggestionResponse
    {
        /// <summary>
        /// Unique identifier of the suggested target location.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Code of the suggested target location.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Code of the zone where the suggested location resides.
        /// </summary>
        public string ZoneCode { get; set; } = null!;

        /// <summary>
        /// Pallets currently stored in the location.
        /// </summary>
        public int CurrentPallets { get; set; }

        /// <summary>
        /// Maximum capacity in pallets of the location.
        /// </summary>
        public int MaxCapacityPallets { get; set; }

        /// <summary>
        /// Remaining capacity in pallets (MaxCapacityPallets - CurrentPallets).
        /// </summary>
        public int RemainingCapacity { get; set; }

        /// <summary>
        /// Suitability score (higher is better) determined by temperature compatibility and item matching rules.
        /// </summary>
        public int SuitabilityScore { get; set; }

        /// <summary>
        /// Match criteria type (e.g., SAME_BATCH, SAME_ITEM, EMPTY, COMPATIBLE).
        /// </summary>
        public string MatchType { get; set; } = null!;
    }

    /// <summary>
    /// Response model grouping putaway location suggestions for a stock item.
    /// </summary>
    public class StockPutawaySuggestionsResponse
    {
        /// <summary>
        /// Unique system identifier of the stock record.
        /// </summary>
        public Guid StockId { get; set; }

        /// <summary>
        /// Code of the product.
        /// </summary>
        public string ItemCode { get; set; } = null!;

        /// <summary>
        /// Lot/Batch number.
        /// </summary>
        public string BatchNumber { get; set; } = null!;

        /// <summary>
        /// Count of pallets needing storage.
        /// </summary>
        public int PalletCount { get; set; }

        /// <summary>
        /// List of recommended locations sorted by suitability.
        /// </summary>
        public List<PutawaySuggestionResponse> Suggestions { get; set; } = new();
    }
}
