using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    public class UpdateMeasurementsRequest
    {
        public List<InboundItemMeasurement> Items { get; set; } = new List<InboundItemMeasurement>();
    }

    public class InboundItemMeasurement
    {
        public string ItemName { get; set; } = null!;
        public string? ItemCode { get; set; }
        public string Unit { get; set; } = null!;
        public decimal ActualQty { get; set; }
        public decimal LengthCm { get; set; }
        public decimal WidthCm { get; set; }
        public decimal HeightCm { get; set; }
        public decimal WeightKg { get; set; }
        public string? ConditionStatus { get; set; }
        public string? Note { get; set; }
        public string? BatchNumber { get; set; }
        public DateOnly? ManufacturedDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}
