using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class PricingMatrix
{
    public Guid PriceId { get; set; }

    public string OriginCity { get; set; } = null!;

    public string DestCity { get; set; } = null!;

    public string PricingUnit { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public decimal? MinCharge { get; set; }

    public DateOnly EffectiveDate { get; set; }
}
