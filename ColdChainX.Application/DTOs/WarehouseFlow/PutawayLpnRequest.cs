namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class PutawayLpnRequest
{
    /// <summary>Kho được chọn để cất hàng — lấy từ danh sách kho hiện có.</summary>
    public Guid WarehouseId { get; set; }

    public string StorageLocation { get; set; } = null!;
}
