namespace ColdChainX.Application.DTOs.Warehouse
{
    public class UpdateWarehouseRequest
    {
        public string WarehouseCode { get; set; } = null!;
        public string WarehouseName { get; set; } = null!;
        public string WarehouseType { get; set; } = null!;
        public string? Address { get; set; }
        public int MaxPallets { get; set; }
        public decimal? DefaultMinTemp { get; set; }
        public decimal? DefaultMaxTemp { get; set; }
        public string Status { get; set; } = "ACTIVE";
    }
}
