using System;

namespace ColdChainX.Core.Entities
{
    public partial class WarehouseZone
    {
        public Guid ZoneId { get; set; }
        public Guid WarehouseId { get; set; }
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
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        public virtual Warehouse Warehouse { get; set; } = null!;
        public virtual ICollection<WarehouseLocation> WarehouseLocations { get; set; } = new List<WarehouseLocation>();
    }
}
