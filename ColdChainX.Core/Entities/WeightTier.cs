using System;

namespace ColdChainX.Core.Entities;

public partial class WeightTier
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public decimal MinWeightKg { get; set; }

    public decimal? MaxWeightKg { get; set; }

    public decimal PricePerKg { get; set; }

    public virtual RouteMaster Route { get; set; } = null!;
}
