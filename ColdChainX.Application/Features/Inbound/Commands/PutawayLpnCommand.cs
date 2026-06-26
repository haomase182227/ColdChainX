using MediatR;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class PutawayLpnCommand : IRequest<PutawayLpnResponse>
{
    public Guid LpnId { get; set; }

    /// <summary>Kho được chọn để cất hàng — lấy từ danh sách kho hiện có.</summary>
    public Guid WarehouseId { get; set; }

    public string StorageLocation { get; set; } = string.Empty;
}

public class PutawayLpnResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
