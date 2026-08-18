namespace ColdChainX.Application.DTOs.Orders
{
    public class CreateOrderResponse
    {
        public Guid OrderId { get; set; }
        public string TrackingCode { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string Category { get; set; } = null!;
        public int Quantity { get; set; }
        public string PackingType { get; set; } = null!;
        public string TempCondition { get; set; } = null!;
        public decimal ExpectedWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public IReadOnlyCollection<OrderPackageVariantResponse> PackageVariants { get; set; } = Array.Empty<OrderPackageVariantResponse>();
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    public class OrderPackageVariantResponse
    {
        public Guid OrderPackageVariantId { get; set; }
        public string? VariantName { get; set; }
        public string PackingType { get; set; } = null!;
        public int Quantity { get; set; }
        public decimal ExpectedUnitWeightKg { get; set; }
        public decimal ExpectedTotalWeightKg { get; set; }
        public decimal ExpectedCbm { get; set; }
        public decimal LengthCm { get; set; }
        public decimal WidthCm { get; set; }
        public decimal HeightCm { get; set; }
        public IReadOnlyCollection<OrderDocumentResponse> LegalDocuments { get; set; } = Array.Empty<OrderDocumentResponse>();
        public IReadOnlyCollection<OrderDocumentResponse> CargoPhotos { get; set; } = Array.Empty<OrderDocumentResponse>();
    }
}

