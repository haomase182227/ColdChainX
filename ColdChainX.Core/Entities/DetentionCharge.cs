using System;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Core.Entities;

public partial class DetentionCharge
{
    [Key]
    public Guid ChargeId { get; set; }
    
    public Guid StopId { get; set; }
    
    public Guid? CustomerId { get; set; }
    
    public int FreeMinutesAllocated { get; set; }
    
    public int ActualWaitMinutes { get; set; }
    
    public decimal AmountCharged { get; set; }
    
    public string Status { get; set; } = null!;
    
    public virtual TripStop Stop { get; set; } = null!;
    
    public virtual Customer? Customer { get; set; }
}
