namespace ColdChainX.Application.DTOs.Orders
{
    public class ReviewOrderRequest
    {
        public string Action { get; set; } = null!;
        public string? RejectReason { get; set; }
        public string? ComplianceCode { get; set; }
    }
}

