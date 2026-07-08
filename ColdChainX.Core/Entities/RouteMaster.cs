using System;
using System.Collections.Generic;
using System.Linq;

namespace ColdChainX.Core.Entities;

public partial class RouteMaster
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = null!;

    public string OriginCity { get; set; } = null!;

    public string DestCity { get; set; } = null!;

    public string TransitTime { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<TransportOrder> TransportOrders { get; set; } = new List<TransportOrder>();

    public virtual ICollection<WeightTier> WeightTiers { get; set; } = new List<WeightTier>();

    public virtual ICollection<RouteStop> RouteStops { get; set; } = new List<RouteStop>();

    public virtual ICollection<RouteSchedule> RouteSchedules { get; set; } = new List<RouteSchedule>();

    public virtual ICollection<MasterTrip> MasterTrips { get; set; } = new List<MasterTrip>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public TimeSpan CutOffTime => RouteSchedules?.FirstOrDefault()?.CutOffTime ?? TimeSpan.Zero;
}
