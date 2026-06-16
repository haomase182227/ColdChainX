namespace ColdChainX.Application.DTOs.WarehouseZone
{
    /// <summary>
    /// Request payload for updating an existing warehouse zone.
    /// </summary>
    public class UpdateWarehouseZoneRequest
    {
        /// <summary>
        /// Code for the zone (e.g., ZONE-A).
        /// </summary>
        public string ZoneCode { get; set; } = null!;

        /// <summary>
        /// Name of the zone (e.g., Deep Freeze Area A).
        /// </summary>
        public string ZoneName { get; set; } = null!;

        /// <summary>
        /// Type of the zone (e.g., Cold, Ambient, Hazmat).
        /// </summary>
        public string ZoneType { get; set; } = null!;

        /// <summary>
        /// Storage classification (e.g., PalletRack, FloorStack).
        /// </summary>
        public string StorageType { get; set; } = null!;

        /// <summary>
        /// Minimum temperature threshold (Celsius) for this zone.
        /// </summary>
        public decimal? TemperatureMin { get; set; }

        /// <summary>
        /// Maximum temperature threshold (Celsius) for this zone.
        /// </summary>
        public decimal? TemperatureMax { get; set; }

        /// <summary>
        /// Maximum pallet capacity of this zone.
        /// </summary>
        public int MaxCapacityPallets { get; set; }

        /// <summary>
        /// Operational status of the zone (e.g., ACTIVE, INACTIVE).
        /// </summary>
        public string Status { get; set; } = "ACTIVE";
    }
}
