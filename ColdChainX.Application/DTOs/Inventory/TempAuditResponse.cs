using System;

namespace ColdChainX.Application.DTOs.Inventory
{
    /// <summary>
    /// Response model representing stocks stored in locations with mismatched temperature settings.
    /// </summary>
    public class TempAuditResponse
    {
        public Guid StockId { get; set; }
        public string ItemCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public decimal? RequiredTempMin { get; set; }
        public decimal? RequiredTempMax { get; set; }
        public decimal? ZoneTemperatureMin { get; set; }
        public decimal? ZoneTemperatureMax { get; set; }
        public string ZoneCode { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
        public string WarehouseName { get; set; } = null!;
    }
}
