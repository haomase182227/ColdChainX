using MediatR;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetInventoryAgingQuery : IRequest<List<InventoryAgingDto>>
{
}

public class InventoryAgingDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string StorageLocation { get; set; } = string.Empty;
    public DateTime? InboundTime { get; set; }
    public DateTime? SlaDeadline { get; set; }
    public string AgingColor { get; set; } = "Green"; // Green, Yellow, Red
    public double HoursInStorage { get; set; }
}
