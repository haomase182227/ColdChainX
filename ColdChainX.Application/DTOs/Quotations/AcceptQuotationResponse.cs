namespace ColdChainX.Application.DTOs.Quotations
{
    public class AcceptQuotationResponse
    {
        public Guid QuoteId { get; set; }
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string? FileUrl { get; set; }
        public string QuoteStatus { get; set; } = null!;
        public string OrderStatus { get; set; } = null!;
    }
}
