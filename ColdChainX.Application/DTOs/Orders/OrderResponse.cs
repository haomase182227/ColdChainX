namespace ColdChainX.Application.DTOs.Orders
{
    public class OrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string ItemName { get; set; } = null!;
        public string Category { get; set; } = null!;
        public string TempCondition { get; set; } = null!;
        public decimal ExpectedWeightKg { get; set; }
        public decimal ActualWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public decimal? ActualCbm { get; set; }
        public decimal CargoValue { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
        public OrderLocationResponse? Destination { get; set; }
        public IReadOnlyCollection<OrderDocumentResponse> Documents { get; set; } = Array.Empty<OrderDocumentResponse>();
        public IReadOnlyCollection<OrderQuotationResponse> Quotations { get; set; } = Array.Empty<OrderQuotationResponse>();
    }

    public class OrderLocationResponse
    {
        public Guid LocationId { get; set; }
        public string LocationName { get; set; } = null!;
        public string Address { get; set; } = null!;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }

    public class OrderDocumentResponse
    {
        public Guid DocId { get; set; }
        public string DocType { get; set; } = null!;
        public string ImageUrl { get; set; } = null!;
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class OrderQuotationResponse
    {
        public Guid QuoteId { get; set; }
        public decimal BaseFreight { get; set; }
        public decimal? LastMileSurcharge { get; set; }
        public decimal? VasAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal FinalAmount { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? CreatedAt { get; set; }
    }
}
