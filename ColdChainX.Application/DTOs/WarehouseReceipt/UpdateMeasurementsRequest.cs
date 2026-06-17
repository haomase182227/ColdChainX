using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    /// <summary>
    /// Request payload to update verified inbound physical measurements of items.
    /// </summary>
    public class UpdateMeasurementsRequest
    {
        /// <summary>
        /// List of item measurements verified at receiving.
        /// </summary>
        public List<InboundItemMeasurement> Items { get; set; } = new List<InboundItemMeasurement>();
    }

    /// <summary>
    /// Details of the physical dimensions and quality parameters for a received item.
    /// </summary>
    public class InboundItemMeasurement
    {
        /// <summary>
        /// Display name of the item.
        /// </summary>
        public string ItemName { get; set; } = null!;

        /// <summary>
        /// Unique code of the item.
        /// </summary>
        public string? ItemCode { get; set; }

        /// <summary>
        /// Unit of measure (e.g. PALLET, BOX, UNIT).
        /// </summary>
        public string Unit { get; set; } = null!;

        /// <summary>
        /// Actual quantity received.
        /// </summary>
        public decimal ActualQty { get; set; }

        /// <summary>
        /// Length in centimeters.
        /// </summary>
        public decimal LengthCm { get; set; }

        /// <summary>
        /// Width in centimeters.
        /// </summary>
        public decimal WidthCm { get; set; }

        /// <summary>
        /// Height in centimeters.
        /// </summary>
        public decimal HeightCm { get; set; }

        /// <summary>
        /// Physical weight in kilograms.
        /// </summary>
        public decimal WeightKg { get; set; }

        /// <summary>
        /// Verified quality condition status (e.g., INTACT, DAMAGED).
        /// </summary>
        public string? ConditionStatus { get; set; }

        /// <summary>
        /// Optional notes.
        /// </summary>
        public string? Note { get; set; }

        /// <summary>
        /// Batch number assigned to the product lot.
        /// </summary>
        public string? BatchNumber { get; set; }

        /// <summary>
        /// Date when the product batch was manufactured.
        /// </summary>
        public DateOnly? ManufacturedDate { get; set; }

        /// <summary>
        /// Expiration date of the product lot.
        /// </summary>
        public DateOnly? ExpiryDate { get; set; }

        /// <summary>
        /// Country where the product was manufactured/produced.
        /// </summary>
        public string CountryOfOrigin { get; set; } = null!;

        /// <summary>
        /// Product classification category.
        /// </summary>
        public ProductCategory ProductCategory { get; set; }
    }
}
