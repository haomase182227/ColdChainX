using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Contracts
{
    public class GenerateContractRequest
    {
        [JsonPropertyName("orderId")]
        public Guid OrderId { get; set; }

        [JsonPropertyName("editedHtmlContent")]
        public string? EditedHtmlContent { get; set; }
    }
}
