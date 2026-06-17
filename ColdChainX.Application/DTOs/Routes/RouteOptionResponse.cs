namespace ColdChainX.Application.DTOs.Routes
{
    public class RouteOptionResponse
    {
        public Guid RouteId { get; set; }
        public string RouteCode { get; set; } = null!;
        public string OriginCity { get; set; } = null!;
        public string DestCity { get; set; } = null!;
        public string TransitTime { get; set; } = null!;
        public TimeSpan CutOffTime { get; set; }
        public string Status { get; set; } = null!;
    }
}
