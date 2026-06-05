using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class GeoFence
{
    public Guid FenceId { get; set; }

    public Guid? LocationId { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Location? Location { get; set; }
}
