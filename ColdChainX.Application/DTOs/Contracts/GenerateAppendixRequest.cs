using System;
using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Contracts
{
    public class GenerateAppendixRequest
    {
        [JsonPropertyName("orderId")]
        public Guid OrderId { get; set; }

        [JsonPropertyName("adjustedPrice")]
        public decimal? AdjustedPrice { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}
