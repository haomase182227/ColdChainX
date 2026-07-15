using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Quotations
{
    public class AcceptQuotationRequest
    {
        public List<Guid>? SelectedServiceCatalogIds { get; set; }
    }

    public class SurchargeItem
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    public class OptionalServiceItem
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public string? Description { get; set; }
    }
}
