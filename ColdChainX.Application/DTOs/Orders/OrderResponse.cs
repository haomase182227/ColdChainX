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
        public decimal? LengthCm { get; set; }
        public decimal? WidthCm { get; set; }
        public decimal? HeightCm { get; set; }
        public string? CbmEstimationMethod { get; set; }
        public string? CbmEstimationConfidence { get; set; }
        public decimal? CustomerProvidedTotalCbm { get; set; }
        public int? TotalPackageQuantity { get; set; }
        public IReadOnlyCollection<OrderPackageLineResponse> PackageLines { get; set; } = Array.Empty<OrderPackageLineResponse>();
        public Guid? DropoffStopId { get; set; }

        public string Status { get; set; } = null!;
        public Guid? MasterTripId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public OrderRouteResponse? Route { get; set; }
        public OrderScheduleResponse? Schedule { get; set; }
        public OrderLocationResponse? Destination { get; set; }
        public IReadOnlyCollection<OrderDocumentResponse> Documents { get; set; } = Array.Empty<OrderDocumentResponse>();

        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerContactName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public IReadOnlyCollection<OrderQuotationResponse> Quotations { get; set; } = Array.Empty<OrderQuotationResponse>();
    }

    public class OrderScheduleResponse
    {
        public Guid ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public DateTime DepartureDate { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan CutOffTime { get; set; }
        public string Status { get; set; } = null!;
    }

    public class OrderScheduleSummaryResponse
    {
        public Guid ScheduleId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public Guid RouteId { get; set; }
        public string RouteCode { get; set; } = null!;
        public string OriginCity { get; set; } = null!;
        public string DestCity { get; set; } = null!;
        public DateTime DepartureDate { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan CutOffTime { get; set; }
        public int TotalOrders { get; set; }
        public int PendingReviewCount { get; set; }
        public int WaitingQuotationCount { get; set; }
        public int WaitingContractCount { get; set; }
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

    public class OrderPackageLineResponse
    {
        public Guid OrderPackageLineId { get; set; }
        public string Label { get; set; } = null!;
        public decimal CapacityKg { get; set; }
        public int Quantity { get; set; }
        public string SizeClass { get; set; } = null!;
    }
}
