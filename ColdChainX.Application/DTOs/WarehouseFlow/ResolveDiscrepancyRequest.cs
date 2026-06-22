namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class ResolveDiscrepancyRequest
{
    public bool AcceptSurcharge { get; set; }

    public string? Note { get; set; }

    public decimal? HandlingFee { get; set; }

    public decimal? StorageFee { get; set; }
}
