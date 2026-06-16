namespace ColdChainX.Application.DTOs.WarehouseLocation
{
    /// <summary>
    /// Request payload for creating a new warehouse location.
    /// </summary>
    public class CreateWarehouseLocationRequest
    {
        /// <summary>
        /// Code for the location (e.g., LOC-01-A-02). Must be unique.
        /// </summary>
        public string LocationCode { get; set; } = null!;

        /// <summary>
        /// Optional code identifying the rack.
        /// </summary>
        public string? RackCode { get; set; }

        /// <summary>
        /// Optional code identifying the bay.
        /// </summary>
        public string? BayCode { get; set; }

        /// <summary>
        /// Optional code identifying the shelf level.
        /// </summary>
        public string? LevelCode { get; set; }

        /// <summary>
        /// Maximum capacity in pallets for this location.
        /// </summary>
        public int MaxCapacityPallets { get; set; }

        /// <summary>
        /// Additional description of the location.
        /// </summary>
        public string? Description { get; set; }
    }
}
