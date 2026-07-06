using System;

namespace ColdChainX.Application.DTOs.Dispatch
{
    public class TripDispatchDto
    {
        public Guid TripId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Vehicle { get; set; } = string.Empty;
        public string Driver { get; set; } = string.Empty;
        public DateTime PlannedStartTime { get; set; }
        public DateTime PlannedEndTime { get; set; }
        public decimal? EstimatedDurationHours { get; set; }
        public int TotalLpns { get; set; }
        public int AllocatedLpns { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
