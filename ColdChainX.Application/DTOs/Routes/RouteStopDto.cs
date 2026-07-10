using System;

namespace ColdChainX.Application.DTOs.Routes
{
    public class RouteStopDto
    {
        public Guid StopId { get; set; }
        public Guid RouteId { get; set; }

        public string StopName { get; set; } = null!;

        public DateTime? CreatedAt { get; set; }
    }

    public class CreateRouteStopRequest
    {

        public string StopName { get; set; } = string.Empty;

    }
}
