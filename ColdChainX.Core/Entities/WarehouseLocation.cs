using System;

namespace ColdChainX.Core.Entities
{
    public partial class WarehouseLocation
    {
        public Guid LocationId { get; set; }
        public Guid ZoneId { get; set; }

        public string LocationCode { get; set; } = null!;
        public string? RackCode { get; set; }
        public string? BayCode { get; set; }
        public string? LevelCode { get; set; }

        public int MaxCapacityPallets { get; set; }
        public int CurrentPallets { get; set; }

        public string Status { get; set; } = null!;
        public string? Description { get; set; }

        // Auditing
        public DateTime? CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        // Navigation
        public virtual WarehouseZone Zone { get; set; } = null!;
    }
}
