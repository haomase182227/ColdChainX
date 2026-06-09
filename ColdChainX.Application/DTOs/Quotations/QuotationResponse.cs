namespace ColdChainX.Application.DTOs.Quotations
{
    public class QuotationResponse
    {
        public Guid QuoteId { get; set; }
        public Guid? OrderId { get; set; }
        public string? TrackingCode { get; set; }
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public decimal BaseFreight { get; set; }
        public decimal? LastMileSurcharge { get; set; }
        public decimal? VasAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string? FileUrl { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
