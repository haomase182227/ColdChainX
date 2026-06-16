using System;

namespace ColdChainX.Application.DTOs.WarehouseZone
{
    /// <summary>
    /// Response model representing a warehouse zone.
    /// </summary>
    public class WarehouseZoneResponse
    {
        /// <summary>
        /// Unique system identifier of the zone.
        /// </summary>
        public Guid ZoneId { get; set; }

        /// <summary>
        /// Unique identifier of the associated warehouse.
        /// </summary>
        public Guid WarehouseId { get; set; }

        /// <summary>
        /// Name of the parent warehouse.
        /// </summary>
        public string WarehouseName { get; set; } = null!;

        /// <summary>
        /// Unique business code for the zone.
        /// </summary>
        public string ZoneCode { get; set; } = null!;

        /// <summary>
        /// Name of the zone.
        /// </summary>
        public string ZoneName { get; set; } = null!;

        /// <summary>
        /// Type of the zone (e.g., Cold, Ambient).
        /// </summary>
        public string ZoneType { get; set; } = null!;

        /// <summary>
        /// Storage classification (e.g. PalletRack).
        /// </summary>
        public string StorageType { get; set; } = null!;

        /// <summary>
        /// Minimum temperature setting (Celsius).
        /// </summary>
        public decimal? TemperatureMin { get; set; }

        /// <summary>
        /// Maximum temperature setting (Celsius).
        /// </summary>
        public decimal? TemperatureMax { get; set; }

        /// <summary>
        /// Maximum pallet capacity of this zone.
        /// </summary>
        public int MaxCapacityPallets { get; set; }

        /// <summary>
        /// Current pallets occupied in this zone.
        /// </summary>
        public int CurrentPallets { get; set; }

        /// <summary>
        /// Operational status of the zone (e.g., ACTIVE).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Timestamp when the zone record was created.
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who created the zone.
        /// </summary>
        public Guid? CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the zone record was last updated.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who last updated the zone.
        /// </summary>
        public Guid? UpdatedBy { get; set; }
    }
}
