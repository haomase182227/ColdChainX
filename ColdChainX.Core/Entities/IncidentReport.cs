using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class IncidentReport
{
    public Guid IncidentId { get; set; }

    public Guid? TripId { get; set; }

    public string IncidentType { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal? CurrentLatitude { get; set; }

    public decimal? CurrentLongitude { get; set; }

    public string? Status { get; set; }

    public Guid ReportedBy { get; set; }

    public DateTime? ReportedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual User ReportedByNavigation { get; set; } = null!;

    public virtual MasterTrip? Trip { get; set; }
}
