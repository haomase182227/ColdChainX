using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Outbound.DTOs;

public class OutboundOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string ServiceType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
}

public class OutboundPickListDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string StorageLocation { get; set; } = null!;
    public int Quantity { get; set; }
    public string Condition { get; set; } = null!;
    public string Status { get; set; } = null!;
}
