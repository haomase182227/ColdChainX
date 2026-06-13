using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class RouteMaster
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = null!;

    public string OriginCity { get; set; } = null!;

    public string DestCity { get; set; } = null!;

    public DateTime Etd { get; set; }

    public int TransitTimeHours { get; set; }

    public DateTime CutOffTime { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<TransportOrder> TransportOrders { get; set; } = new List<TransportOrder>();
}
