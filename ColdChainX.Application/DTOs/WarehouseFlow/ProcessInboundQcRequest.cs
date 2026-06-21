namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class ProcessInboundQcRequest
{
    public decimal ActualWeightKg { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public decimal RecordedTemperature { get; set; }

    public string DelivererName { get; set; } = null!;

    public string? BatchNumber { get; set; }

    public string? Note { get; set; }
}
