namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class CompleteTripLoadingRequest
{
    public string SealNumber { get; set; } = null!;

    public List<Guid> LpnIds { get; set; } = new();
}
