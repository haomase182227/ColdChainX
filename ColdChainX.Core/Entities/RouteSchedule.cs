using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class RouteSchedule
{
    public Guid ScheduleId { get; set; }

    public Guid RouteId { get; set; }

    public string ScheduleName { get; set; } = null!;

    /// <summary>
    /// 1 = Monday, 2 = Tuesday, ..., 7 = Sunday
    /// </summary>
    public int DayOfWeek { get; set; }

    public TimeSpan DepartureTime { get; set; }

    public TimeSpan CutOffTime { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual RouteMaster Route { get; set; } = null!;

    public virtual ICollection<MasterTrip> MasterTrips { get; set; } = new List<MasterTrip>();
}
