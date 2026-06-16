using System;
using System.Collections.Generic;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.CycleCount
{
    /// <summary>
    /// Response model representing a cycle count plan containing audit targets and status.
    /// </summary>
    public class CycleCountPlanResponse
    {
        /// <summary>
        /// Unique system identifier of the cycle count plan.
        /// </summary>
        public Guid PlanId { get; set; }

        /// <summary>
        /// System-generated plan business code.
        /// </summary>
        public string PlanCode { get; set; } = null!;

        /// <summary>
        /// Status of the cycle count plan (e.g. DRAFT, IN_PROGRESS, COMPLETED).
        /// </summary>
        public CycleCountPlanStatus Status { get; set; }

        /// <summary>
        /// Unique identifier of the assigned operator.
        /// </summary>
        public Guid? AssignedToUserId { get; set; }

        /// <summary>
        /// Username of the assigned operator.
        /// </summary>
        public string? AssignedToUsername { get; set; }

        /// <summary>
        /// Unique identifier of the warehouse being audited.
        /// </summary>
        public Guid WarehouseId { get; set; }

        /// <summary>
        /// Name of the warehouse.
        /// </summary>
        public string WarehouseName { get; set; } = null!;

        /// <summary>
        /// Description guidelines or instructions.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Timestamp when the plan was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Unique identifier of the manager who created the plan.
        /// </summary>
        public Guid CreatedBy { get; set; }

        /// <summary>
        /// Timestamp when the audit plan was completed/closed.
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Unique identifier of the user who completed the plan.
        /// </summary>
        public Guid? CompletedBy { get; set; }
        
        /// <summary>
        /// List of locations and expected stock target entries to count.
        /// </summary>
        public List<CycleCountEntryResponse> Entries { get; set; } = new();
    }
}
