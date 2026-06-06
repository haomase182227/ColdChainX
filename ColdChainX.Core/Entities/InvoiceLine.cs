using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class InvoiceLine
{
    public Guid LineId { get; set; }

    public Guid InvoiceId { get; set; }

    public Guid OrderId { get; set; }

    public string ChargeType { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal? Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public decimal? TaxRate { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;

    public virtual TransportOrder Order { get; set; } = null!;
}
