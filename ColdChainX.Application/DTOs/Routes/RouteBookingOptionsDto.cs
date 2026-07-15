using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Routes
{
    public class RouteBookingOptionsDto
    {
        public Guid RouteId { get; set; }
        public IReadOnlyCollection<ScheduleOptionDto> AvailableSchedules { get; set; } = new List<ScheduleOptionDto>();
        public IReadOnlyCollection<StopOptionDto> AvailableStops { get; set; } = new List<StopOptionDto>();
    }

    public class ScheduleOptionDto
    {
        public Guid ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public DateOnly DepartureDate { get; set; }
        public TimeOnly DepartureTime { get; set; }
        public TimeOnly CutOffTime { get; set; }
    }

    public class StopOptionDto
    {
        public Guid StopId { get; set; }
        public string StopName { get; set; } = null!;
    }
}
