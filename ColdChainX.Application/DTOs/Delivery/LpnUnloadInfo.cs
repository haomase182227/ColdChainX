using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class LpnUnloadInfo
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public int Quantity { get; set; }
    public int UnloadOrder { get; set; }
    public string TempCondition { get; set; } = null!;
}
