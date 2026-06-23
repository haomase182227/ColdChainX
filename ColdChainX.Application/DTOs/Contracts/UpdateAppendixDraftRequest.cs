using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Contracts
{
    public class UpdateAppendixDraftRequest
    {
        [JsonPropertyName("editedHtmlContent")]
        public string EditedHtmlContent { get; set; } = null!;
    }
}
