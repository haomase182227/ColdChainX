using System;

namespace ColdChainX.Core.Entities;

public partial class OrderPackageLine
{
    public Guid OrderPackageLineId { get; set; }

    public Guid OrderId { get; set; }

    public string Label { get; set; } = null!;

    public decimal CapacityKg { get; set; }

    public int Quantity { get; set; }

    public string SizeClass { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;
}
