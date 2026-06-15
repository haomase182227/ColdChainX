using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.CycleCount
{
    public class CycleCountPlanResponse
    {
        public Guid PlanId { get; set; }
        public string PlanCode { get; set; } = null!;
        public CycleCountPlanStatus Status { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public string? AssignedToUsername { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedBy { get; set; }
        
        public List<CycleCountEntryResponse> Entries { get; set; } = new();
    }
}
