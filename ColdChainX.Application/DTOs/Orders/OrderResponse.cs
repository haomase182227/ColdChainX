namespace ColdChainX.Application.DTOs.Orders
{
    public class OrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Category { get; set; } = null!;
        public int Quantity { get; set; }
        public string PackingType { get; set; } = null!;
        public string TempCondition { get; set; } = null!;
        public decimal ExpectedWeightKg { get; set; }
        public decimal ActualWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public decimal? ActualCbm { get; set; }

        public string Status { get; set; } = null!;
        public Guid? MasterTripId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public OrderRouteResponse? Route { get; set; }
        public OrderLocationResponse? Destination { get; set; }
        public IReadOnlyCollection<OrderDocumentResponse> Documents { get; set; } = Array.Empty<OrderDocumentResponse>();
    }

    public class OrderRouteResponse
    {
        public Guid RouteId { get; set; }
        public string RouteCode { get; set; } = null!;
        public string OriginCity { get; set; } = null!;
        public string DestCity { get; set; } = null!;
        public string TransitTime { get; set; } = null!;
        public TimeSpan CutOffTime { get; set; }
    }

    public class OrderLocationResponse
    {
        public Guid LocationId { get; set; }
        public string Address { get; set; } = null!;
    }

    public class OrderDocumentResponse
    {
        public Guid DocId { get; set; }
        public string DocType { get; set; } = null!;
        public string ImageUrl { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }

    public class OrderQuotationResponse
    {
        public Guid QuoteId { get; set; }
        public decimal BaseFreight { get; set; }
        public decimal? LastMileSurcharge { get; set; }
        public decimal? VatPercentage { get; set; }
        public decimal VatAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string? FileUrl { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
