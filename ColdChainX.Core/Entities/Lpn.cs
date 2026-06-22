using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities;

public partial class Lpn
{
    public Guid LpnId { get; set; }

    public string LpnCode { get; set; } = null!;

    public Guid OrderId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid ReceiptId { get; set; }

    public Guid? RouteId { get; set; }

    public Guid? TripId { get; set; }

    public int Quantity { get; set; }

    public decimal ActualWeightKg { get; set; }

    public decimal ActualCbm { get; set; }

    public decimal? RequiredTemperature { get; set; }

    public decimal? RecordedTemperature { get; set; }

    public string? StorageLocation { get; set; }

    public LpnState State { get; set; }

    public string? DiscrepancyReason { get; set; }

    public string? EvidenceImageUrl { get; set; }

    public DateTime? InboundTime { get; set; }

    public DateTime? SlaDeadline { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual RouteMaster? Route { get; set; }

    public virtual MasterTrip? Trip { get; set; }

    public virtual WarehouseReceipt Receipt { get; set; } = null!;

    public virtual ICollection<PenaltyBill> PenaltyBills { get; set; } = new List<PenaltyBill>();
}
