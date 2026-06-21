using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Entities;

public partial class Lpn
{
    public Guid LpnId { get; set; }

    public string LpnCode { get; set; } = null!;

    public Guid OrderId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? RouteId { get; set; }

    public Guid? TripId { get; set; }

    public string ItemName { get; set; } = null!;

    public string? ItemCode { get; set; }

    public string? BatchNumber { get; set; }

    public string? StorageLocation { get; set; }

    public int Quantity { get; set; }

    public decimal ExpectedWeightKg { get; set; }

    public decimal ActualWeightKg { get; set; }

    public decimal ExpectedCbm { get; set; }

    public decimal ActualCbm { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public decimal? RequiredTemperature { get; set; }

    public decimal? RecordedTemperature { get; set; }

    public decimal MaxDiffPercent { get; set; }

    public LpnState State { get; set; }

    public string? DiscrepancyReason { get; set; }

    public string? GrnPdfUrl { get; set; }

    public string? DiscrepancyPdfUrl { get; set; }

    public string? ReturnPdfUrl { get; set; }

    public DateTime? InboundTime { get; set; }

    public DateTime? PickedAt { get; set; }

    public DateTime? ShippedAt { get; set; }

    public DateTime? SlaDeadline { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual Customer? Customer { get; set; }

    public virtual RouteMaster? Route { get; set; }

    public Guid? ReceiptItemId { get; set; }

    public virtual MasterTrip? Trip { get; set; }

    public virtual WarehouseReceiptItem? ReceiptItem { get; set; }

    public virtual ICollection<PenaltyBill> PenaltyBills { get; set; } = new List<PenaltyBill>();
}
