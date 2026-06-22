using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Features.Inbound.DTOs;

public class InboundReceiptDto
{
    public Guid ReceiptId { get; set; }
    public string ReceiptCode { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string? Status { get; set; }
    public DateTime? ArrivalTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public string DriverName { get; set; } = null!;
    public string TruckPlate { get; set; } = null!;
}

public class InboundReceiptDetailDto : InboundReceiptDto
{
    public List<InboundReceiptItemDto> Items { get; set; } = new();
}

public class InboundReceiptItemDto
{
    public Guid ReceiptItemId { get; set; }
    public string ItemName { get; set; } = null!;
    public int ExpectedQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public string ConditionStatus { get; set; } = null!;
}
