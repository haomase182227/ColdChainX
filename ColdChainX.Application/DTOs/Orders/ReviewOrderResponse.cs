using System.Text.Json.Serialization;

namespace ColdChainX.Application.DTOs.Orders
{
    public class ReviewOrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string Status { get; set; } = null!;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? QuoteId { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? BaseFreight { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? LastMileSurcharge { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? VatAmount { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? FinalAmount { get; set; }
    }
}
