namespace ColdChainX.Core.Entities;

/// <summary>
/// The quantity and QC measurements of one package size packed inside an LPN.
/// An LPN can contain many sizes and a size can be split across several LPNs.
/// </summary>
public class LpnPackageVariantLine
{
    public Guid LpnPackageVariantLineId { get; set; }
    public Guid LpnId { get; set; }
    public Guid? OrderPackageVariantId { get; set; }
    public string? VariantName { get; set; }
    public string PackingType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ExpectedCbm { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? RecordedTemperature { get; set; }
    public decimal DiffPercent { get; set; }
    public bool HasDiscrepancy { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Lpn Lpn { get; set; } = null!;
    public virtual OrderPackageVariant? OrderPackageVariant { get; set; }
}
