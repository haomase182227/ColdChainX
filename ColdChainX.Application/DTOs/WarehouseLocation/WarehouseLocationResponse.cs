using System;

namespace ColdChainX.Application.DTOs.WarehouseLocation
{
    /// <summary>
    /// Response model representing a warehouse location.
    /// </summary>
    public class WarehouseLocationResponse
    {
        /// <summary>
        /// Unique system identifier of the location.
        /// </summary>
        public Guid LocationId { get; set; }

        /// <summary>
        /// Unique identifier of the associated zone.
        /// </summary>
        public Guid ZoneId { get; set; }

        /// <summary>
        /// Name of the zone where the location resides.
        /// </summary>
        public string ZoneName { get; set; } = null!;

        /// <summary>
        /// Name of the parent warehouse.
        /// </summary>
        public string WarehouseName { get; set; } = null!;

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
        /// Current pallets occupied at this location.
        /// </summary>
        public int CurrentPallets { get; set; }

        /// <summary>
        /// Operational status of the location (e.g., ACTIVE, DAMAGED).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Additional description of the location.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Timestamp when the location was created.
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who created this location.
        /// </summary>
        public Guid? CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the location was last updated.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who last updated this location.
        /// </summary>
        public Guid? UpdatedBy { get; set; }
    }
}
