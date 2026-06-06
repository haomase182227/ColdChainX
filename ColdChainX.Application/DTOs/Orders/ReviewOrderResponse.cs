namespace ColdChainX.Application.DTOs.Orders
{
    public class ReviewOrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string Status { get; set; } = null!;
        public Guid? QuoteId { get; set; }
        public decimal? BaseFreight { get; set; }
        public decimal? LastMileSurcharge { get; set; }
        public decimal? VasAmount { get; set; }
        public decimal? VatAmount { get; set; }
        public decimal? FinalAmount { get; set; }
    }
}
