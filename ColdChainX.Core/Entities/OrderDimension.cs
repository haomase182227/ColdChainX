using System;

namespace ColdChainX.Core.Entities;

public partial class OrderDimension
{
    public Guid OrderId { get; set; }
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ExpectedCbm { get; set; }
    public decimal? ActualCbm { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;
}
