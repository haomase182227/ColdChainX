using System;

namespace ColdChainX.Application.DTOs.Warehouse
{
    public class WarehouseResponse
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = null!;
        public string WarehouseName { get; set; } = null!;
        public string WarehouseType { get; set; } = null!;
        public string? Address { get; set; }
        public int MaxPallets { get; set; }
        public int? CurrentPallets { get; set; }
        public decimal? DefaultMinTemp { get; set; }
        public decimal? DefaultMaxTemp { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
