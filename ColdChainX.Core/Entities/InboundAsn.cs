using System;

namespace ColdChainX.Core.Entities;

public partial class InboundAsn
{
    public Guid AsnId { get; set; }

    public string AsnCode { get; set; } = null!;

    public Guid OrderId { get; set; }

    public DateTime RequestedDropoffTime { get; set; }

    public string QrCodeValue { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Phone { get; set; }

    public Guid? WarehouseId { get; set; }

    public Guid? CustomerId { get; set; }

    public string? FileUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;
}
