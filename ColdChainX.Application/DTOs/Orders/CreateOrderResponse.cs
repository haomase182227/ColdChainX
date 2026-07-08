namespace ColdChainX.Application.DTOs.Orders
{
    public class CreateOrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public Guid DestLocationId { get; set; }
        public decimal ExpectedCbm { get; set; }
        
        public string Status { get; set; } = null!;
    }
}

