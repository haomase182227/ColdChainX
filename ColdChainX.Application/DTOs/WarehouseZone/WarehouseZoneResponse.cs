using System;

namespace ColdChainX.Application.DTOs.WarehouseZone
{
    public class WarehouseZoneResponse
    {
        public Guid ZoneId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string ZoneCode { get; set; } = null!;
        public string ZoneName { get; set; } = null!;
        public string ZoneType { get; set; } = null!;
        public string StorageType { get; set; } = null!;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public int MaxCapacityPallets { get; set; }
        public int CurrentPallets { get; set; }
        public string Status { get; set; } = null!;

        // Auditing
        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
