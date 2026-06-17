using System;

namespace ColdChainX.Application.DTOs.Warehouse
{
    /// <summary>
    /// Response model representing a warehouse detail.
    /// </summary>
    public class WarehouseResponse
    {
        /// <summary>
        /// Unique system identifier of the warehouse.
        /// </summary>
        public Guid WarehouseId { get; set; }

        /// <summary>
        /// Unique business code for the warehouse.
        /// </summary>
        public string WarehouseCode { get; set; } = null!;

        /// <summary>
        /// Name of the warehouse.
        /// </summary>
        public string WarehouseName { get; set; } = null!;

        /// <summary>
        /// Type of storage (e.g., ColdStorage, DryStorage).
        /// </summary>
        public string WarehouseType { get; set; } = null!;

        /// <summary>
        /// Physical address details.
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// Maximum capacity in pallets.
        /// </summary>
        public int MaxPallets { get; set; }

        /// <summary>
        /// Current capacity in pallets currently utilized.
        /// </summary>
        public int? CurrentPallets { get; set; }

        /// <summary>
        /// Standard minimum temperature setting.
        /// </summary>
        public decimal? DefaultMinTemp { get; set; }

        /// <summary>
        /// Standard maximum temperature setting.
        /// </summary>
        public decimal? DefaultMaxTemp { get; set; }

        /// <summary>
        /// Warehouse operational status (e.g. ACTIVE).
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Timestamp when the warehouse record was created.
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who created the warehouse.
        /// </summary>
        public Guid? CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the warehouse record was last updated.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who last updated the warehouse.
        /// </summary>
        public Guid? UpdatedBy { get; set; }
    }
}
