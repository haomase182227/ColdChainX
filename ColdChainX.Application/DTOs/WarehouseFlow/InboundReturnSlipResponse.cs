using System;
using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.WarehouseFlow
{
    public class InboundReturnSlipResponse
    {
        [JsonPropertyName("returnSlipId")]
        public Guid ReturnSlipId { get; set; }

        [JsonPropertyName("orderId")]
        public Guid OrderId { get; set; }

        [JsonPropertyName("lpnId")]
        public Guid LpnId { get; set; }

        [JsonPropertyName("slipCode")]
        public string SlipCode { get; set; } = null!;

        [JsonPropertyName("returnedWeightKg")]
        public decimal ReturnedWeightKg { get; set; }

        [JsonPropertyName("returnedCbm")]
        public decimal ReturnedCbm { get; set; }

        [JsonPropertyName("returnedQty")]
        public int ReturnedQty { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("pdfUrl")]
        public string? PdfUrl { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}
