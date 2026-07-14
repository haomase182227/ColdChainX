namespace ColdChainX.Application.DTOs.Quotations
{
    public class EditQuotationRequest
    {
        public decimal? BaseFreight { get; set; }
        public decimal? LastMileSurcharge { get; set; }
        public decimal? VatPercentage { get; set; }
        public IReadOnlyCollection<QuotationAdditionalChargeRequest>? AdditionalCharges { get; set; }
        public string? OverrideReason { get; set; }
    }

    public class QuotationAdditionalChargeRequest
    {
        public Guid ServiceCatalogId { get; set; }
    }
}
