using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Quotation
{
    public Guid QuoteId { get; set; }

    public Guid? OrderId { get; set; }

    public decimal BaseFreight { get; set; }

    public decimal? LastMileSurcharge { get; set; }

    public decimal? VasAmount { get; set; }

    public decimal VatAmount { get; set; }

    public decimal FinalAmount { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder? Order { get; set; }
}
