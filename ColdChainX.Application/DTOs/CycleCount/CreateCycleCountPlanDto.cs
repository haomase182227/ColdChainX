using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.CycleCount
{
    /// <summary>
    /// Request payload to create a new cycle count audit plan.
    /// </summary>
    public class CreateCycleCountPlanDto
    {
        /// <summary>
        /// Unique system identifier of the target warehouse.
        /// </summary>
        public Guid WarehouseId { get; set; }

        /// <summary>
        /// Unique identifier of the user/operator assigned to perform the counting (optional).
        /// </summary>
        public Guid? AssignedToUserId { get; set; }

        /// <summary>
        /// Description notes or guidelines for the counting team.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// List of specific zone IDs to audit. If null or empty, the system targets the whole warehouse.
        /// </summary>
        public List<Guid>? ZoneIds { get; set; }

        /// <summary>
        /// List of specific location IDs to audit. If null or empty, targets all locations within specified zones.
        /// </summary>
        public List<Guid>? LocationIds { get; set; }
    }
}
