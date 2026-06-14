namespace ColdChainX.Application.DTOs.Quotations
{
    public class EditQuotationRequest
    {
        public decimal BaseFreight { get; set; }
        public decimal LastMileSurcharge { get; set; }
        public decimal VatPercentage { get; set; } = 8m;
        public string? OverrideReason { get; set; }
    }
}
