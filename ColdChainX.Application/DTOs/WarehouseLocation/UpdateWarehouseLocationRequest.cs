namespace ColdChainX.Application.DTOs.WarehouseLocation
{
    public class UpdateWarehouseLocationRequest
    {
        public string LocationCode { get; set; } = null!;
        public string? RackCode { get; set; }
        public string? BayCode { get; set; }
        public string? LevelCode { get; set; }
        public int MaxCapacityPallets { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
    }
}
