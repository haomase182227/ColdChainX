namespace ColdChainX.Application.DTOs.Orders
{
    public class ReviewOrderRequest
    {
        public string Action { get; set; } = null!;
        public string? CustomerNote { get; set; }
    }
}

