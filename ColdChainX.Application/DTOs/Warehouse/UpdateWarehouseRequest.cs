namespace ColdChainX.Application.DTOs.Warehouse
{
    /// <summary>
    /// Request payload for updating an existing warehouse.
    /// </summary>
    public class UpdateWarehouseRequest
    {
        /// <summary>
        /// Unique code identifying the warehouse (e.g., WH-001).
        /// </summary>
        public string WarehouseCode { get; set; } = null!;

        /// <summary>
        /// Name of the warehouse.
        /// </summary>
        public string WarehouseName { get; set; } = null!;

        /// <summary>
        /// Type/classification of the warehouse (e.g., ColdStorage, DryStorage).
        /// </summary>
        public string WarehouseType { get; set; } = null!;

        /// <summary>
        /// Physical address of the warehouse.
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// Maximum pallet capacity of the warehouse.
        /// </summary>
        public int MaxPallets { get; set; }

        /// <summary>
        /// Default minimum allowed temperature (Celsius) for cold chain compliance.
        /// </summary>
        public decimal? DefaultMinTemp { get; set; }

        /// <summary>
        /// Default maximum allowed temperature (Celsius) for cold chain compliance.
        /// </summary>
        public decimal? DefaultMaxTemp { get; set; }

        /// <summary>
        /// Operational status of the warehouse (e.g., ACTIVE, INACTIVE).
        /// </summary>
        public string Status { get; set; } = "ACTIVE";
    }
}
