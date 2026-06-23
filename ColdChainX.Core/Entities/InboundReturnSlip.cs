using System;

namespace ColdChainX.Core.Entities;

public partial class InboundReturnSlip
{
    public Guid ReturnSlipId { get; set; }

    public Guid OrderId { get; set; }

    public Guid LpnId { get; set; }

    public string SlipCode { get; set; } = null!;

    public decimal ReturnedWeightKg { get; set; }

    public decimal ReturnedCbm { get; set; }

    public int ReturnedQty { get; set; }

    public string? Reason { get; set; }

    public string? PdfUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual Lpn Lpn { get; set; } = null!;
}
