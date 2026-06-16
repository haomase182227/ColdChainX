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
        public string Name { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }
}
