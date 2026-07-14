using System;

namespace ColdChainX.Application.DTOs.Routes
{
    public class RouteScheduleDto
    {
        public Guid ScheduleId { get; set; }
        public Guid RouteId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public DateOnly DepartureDate { get; set; }
        public TimeOnly DepartureTime { get; set; }
        public TimeOnly CutOffTime { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateRouteScheduleRequest
    {
        public DateOnly DepartureDate { get; set; }
        public TimeOnly DepartureTime { get; set; }
        public TimeOnly CutOffTime { get; set; }
    }

    public class UpdateRouteScheduleRequest
    {
        public DateOnly DepartureDate { get; set; }
        public TimeOnly DepartureTime { get; set; }
        public TimeOnly CutOffTime { get; set; }
        public string Status { get; set; } = null!;
    }
}
