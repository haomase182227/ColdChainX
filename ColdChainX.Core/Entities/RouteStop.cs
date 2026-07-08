using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class RouteStop
{
    public Guid StopId { get; set; }

    public Guid RouteId { get; set; }

    public string StopName { get; set; } = null!;



    public DateTime? CreatedAt { get; set; }

    public virtual RouteMaster Route { get; set; } = null!;
}
