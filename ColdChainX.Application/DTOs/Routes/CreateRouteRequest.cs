namespace ColdChainX.Application.DTOs.Routes
{
    public class CreateRouteRequest
    {
        public string RouteCode { get; set; } = null!;
        public string OriginCity { get; set; } = null!;
        public string DestCity { get; set; } = null!;
        public string TransitTime { get; set; } = null!;
    }

    public class UpdateRouteRequest
    {
        public string RouteCode { get; set; } = null!;
        public string OriginCity { get; set; } = null!;
        public string DestCity { get; set; } = null!;
        public string TransitTime { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
