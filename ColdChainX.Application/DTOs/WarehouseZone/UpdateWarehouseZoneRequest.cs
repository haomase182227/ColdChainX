namespace ColdChainX.Application.DTOs.WarehouseZone
{
    public class UpdateWarehouseZoneRequest
    {
        public string ZoneCode { get; set; } = null!;
        public string ZoneName { get; set; } = null!;
        public string ZoneType { get; set; } = null!;
        public string StorageType { get; set; } = null!;
        public decimal? TemperatureMin { get; set; }
        public decimal? TemperatureMax { get; set; }
        public int MaxCapacityPallets { get; set; }
        public string Status { get; set; } = "ACTIVE";
    }
}
