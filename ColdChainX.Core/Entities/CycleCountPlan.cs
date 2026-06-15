using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities
{
    public class CycleCountPlan
    {
        public Guid PlanId { get; set; }
        public string PlanCode { get; set; } = null!;
        public CycleCountPlanStatus Status { get; set; } = CycleCountPlanStatus.DRAFT;
        public Guid? AssignedToUserId { get; set; }
        public Guid WarehouseId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedBy { get; set; }

        public virtual Warehouse Warehouse { get; set; } = null!;
        public virtual User? AssignedToUser { get; set; }
        public virtual User Creator { get; set; } = null!;
        public virtual User? Completer { get; set; }
        public virtual ICollection<CycleCountEntry> Entries { get; set; } = new List<CycleCountEntry>();
    }
}
