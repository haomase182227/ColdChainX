using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.CycleCount
{
    public class CreateCycleCountPlanDto
    {
        public Guid WarehouseId { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public string? Notes { get; set; }
        public List<Guid>? ZoneIds { get; set; }
        public List<Guid>? LocationIds { get; set; }
    }
}
