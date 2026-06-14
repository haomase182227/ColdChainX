using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class RouteMaster
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = null!;

    public string OriginCity { get; set; } = null!;

    public string DestCity { get; set; } = null!;

    public string TransitTime { get; set; } = null!;

    public TimeSpan CutOffTime { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<TransportOrder> TransportOrders { get; set; } = new List<TransportOrder>();

    public virtual ICollection<WeightTier> WeightTiers { get; set; } = new List<WeightTier>();
}
