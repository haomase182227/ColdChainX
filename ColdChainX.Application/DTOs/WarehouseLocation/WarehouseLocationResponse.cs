using System;

namespace ColdChainX.Application.DTOs.WarehouseLocation
{
    public class WarehouseLocationResponse
    {
        public Guid LocationId { get; set; }
        public Guid ZoneId { get; set; }
        public string ZoneName { get; set; } = null!;
        public string WarehouseName { get; set; } = null!;

        public string LocationCode { get; set; } = null!;
        public string? RackCode { get; set; }
        public string? BayCode { get; set; }
        public string? LevelCode { get; set; }

        public int MaxCapacityPallets { get; set; }
        public int CurrentPallets { get; set; }

        public string Status { get; set; } = null!;
        public string? Description { get; set; }

        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
