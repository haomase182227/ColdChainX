namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class PenaltyBillResponse
{
    public Guid PenaltyBillId { get; set; }

    public string BillCode { get; set; } = null!;

    public Guid LpnId { get; set; }

    public Guid OrderId { get; set; }

    public decimal HandlingFee { get; set; }

    public decimal StorageFee { get; set; }

    public decimal TotalAmount { get; set; }

    public string Reason { get; set; } = null!;

    public bool IsPaid { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }
}
