using System;

namespace ColdChainX.Core.Entities;

public partial class InboundQcPackageLine
{
    public Guid InboundQcPackageLineId { get; set; }

    public Guid OrderId { get; set; }

    public Guid AsnId { get; set; }

    public Guid? LpnId { get; set; }

    public string Label { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal ActualWeightKg { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public decimal ActualCbm { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual InboundAsn Asn { get; set; } = null!;

    public virtual Lpn? Lpn { get; set; }
}
