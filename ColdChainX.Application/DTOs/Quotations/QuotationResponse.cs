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
        public IReadOnlyCollection<QuotationAdditionalChargeResponse> AdditionalCharges { get; set; } = Array.Empty<QuotationAdditionalChargeResponse>();
        public decimal AdditionalChargesTotal { get; set; }
        public decimal? VatPercentage { get; set; }
        public decimal VatAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public decimal? ChargeableWeightKg { get; set; }
        public decimal? VolumetricWeightKg { get; set; }
        public decimal? PricePerKg { get; set; }
        public decimal? DistanceKm { get; set; }
        public decimal? SystemBaseFreight { get; set; }
        public decimal? ManualAdjustment { get; set; }
        public string? OverrideReason { get; set; }
        public string PricingSource { get; set; } = null!;
        public string? FileUrl { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }

    public class QuotationAdditionalChargeResponse
    {
        public Guid? ServiceCatalogId { get; set; }
        public string Name { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }
}
