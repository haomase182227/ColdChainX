namespace ColdChainX.Core.Entities;

public partial class PenaltyBill
{
    public Guid PenaltyBillId { get; set; }

    public string BillCode { get; set; } = null!;

    public Guid LpnId { get; set; }

    public Guid OrderId { get; set; }

    public Guid? CustomerId { get; set; }

    public decimal HandlingFee { get; set; }

    public decimal StorageFee { get; set; }

    public decimal TotalAmount { get; set; }

    public string Reason { get; set; } = null!;

    public bool IsPaid { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public Guid? PaidBy { get; set; }

    public virtual Lpn Lpn { get; set; } = null!;

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual User? PaidByNavigation { get; set; }
}
