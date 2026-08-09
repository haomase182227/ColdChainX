namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class PutawayLpnRequest
{
    public Guid WarehouseId { get; set; }

    public string StorageLocation { get; set; } = null!;
}
