using System;

namespace ColdChainX.Application.DTOs.Routes
{
    public class RouteScheduleDto
    {
        public Guid ScheduleId { get; set; }
        public Guid RouteId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public int DayOfWeek { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan CutOffTime { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateRouteScheduleRequest
    {
        public string ScheduleName { get; set; } = null!;
        public int DayOfWeek { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan CutOffTime { get; set; }
    }
}
