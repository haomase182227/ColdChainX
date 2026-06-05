using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class ReturnedItem
{
    public Guid ReturnId { get; set; }

    public Guid? EpodId { get; set; }

    public string ItemName { get; set; } = null!;

    public string? ItemCode { get; set; }

    public string Unit { get; set; } = null!;

    public decimal ReturnedQty { get; set; }

    public string ReasonType { get; set; } = null!;

    public string? ReasonNote { get; set; }

    public string? ProcessingStatus { get; set; }

    public Guid? ProcessedBy { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime? ReturnedAt { get; set; }

    public virtual DeliveryEpod? Epod { get; set; }

    public virtual User? ProcessedByNavigation { get; set; }
}
