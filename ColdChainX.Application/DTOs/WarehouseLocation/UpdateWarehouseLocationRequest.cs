namespace ColdChainX.Application.DTOs.WarehouseLocation
{
    /// <summary>
    /// Request payload for updating an existing warehouse location.
    /// </summary>
    public class UpdateWarehouseLocationRequest
    {
        /// <summary>
        /// Code for the location (e.g., LOC-01-A-02).
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

        /// <summary>
        /// Operational status of the location (e.g., ACTIVE, INACTIVE, DAMAGED).
        /// </summary>
        public string Status { get; set; } = null!;
    }
}
