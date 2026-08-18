namespace ColdChainX.Core.Entities;

public partial class OrderPackageVariant
{
    public Guid OrderPackageVariantId { get; set; }

    public Guid OrderId { get; set; }

    public string? VariantName { get; set; }

    public string PackingType { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal ExpectedUnitWeightKg { get; set; }

    public decimal ExpectedTotalWeightKg { get; set; }

    public decimal ExpectedCbm { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual ICollection<TransportDocument> TransportDocuments { get; set; } = new List<TransportDocument>();

    public virtual ICollection<LpnPackageVariantLine> LpnPackageVariantLines { get; set; } = new List<LpnPackageVariantLine>();
}
