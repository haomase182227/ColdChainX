using System;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Core.Entities;

public partial class TripStopEvent
{
    [Key]
    public Guid EventId { get; set; }
    
    public Guid StopId { get; set; }
    
    public string EventType { get; set; } = null!;
    
    public DateTime EventTime { get; set; }
    
    public string? MetaData { get; set; }
    
    public virtual TripStop Stop { get; set; } = null!;
}
