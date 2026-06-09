using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Quotations
{
    public class AcceptQuotationRequest
    {
        [JsonPropertyName("Customer_ID")]
        public Guid CustomerId { get; set; }
    }
}
