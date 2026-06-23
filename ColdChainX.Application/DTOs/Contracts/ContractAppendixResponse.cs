using System;
using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Contracts
{
    public class ContractAppendixResponse
    {
        [JsonPropertyName("appendixId")]
        public Guid AppendixId { get; set; }

        [JsonPropertyName("contractId")]
        public Guid? ContractId { get; set; }

        [JsonPropertyName("orderId")]
        public Guid OrderId { get; set; }

        [JsonPropertyName("appendixNumber")]
        public string AppendixNumber { get; set; } = null!;

        [JsonPropertyName("adjustedPrice")]
        public decimal AdjustedPrice { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = null!;

        [JsonPropertyName("draftHtmlContent")]
        public string? DraftHtmlContent { get; set; }

        [JsonPropertyName("pdfUrl")]
        public string? PdfUrl { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("sentAt")]
        public DateTime? SentAt { get; set; }

        [JsonPropertyName("resolvedAt")]
        public DateTime? ResolvedAt { get; set; }
    }
}
