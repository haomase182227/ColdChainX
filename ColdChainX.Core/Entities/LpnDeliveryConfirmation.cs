using System;

namespace ColdChainX.Core.Entities;

public partial class LpnDeliveryConfirmation
{
    public Guid ConfirmationId { get; set; }

    public Guid LpnId { get; set; }

    public Guid TripId { get; set; }

    public Guid OrderId { get; set; }

    public string OutcomeType { get; set; } = null!; // "DELIVERED" | "REJECTED"

    public string? ReceiverName { get; set; }

    public string? ReceiverPhone { get; set; }

    public string? RejectReason { get; set; }

    public string? RejectNote { get; set; }

    public string EvidenceImageUrl { get; set; } = null!;

    public Guid ConfirmedByDriverId { get; set; }

    public DateTime ConfirmedAt { get; set; }

    public virtual Lpn Lpn { get; set; } = null!;

    public virtual MasterTrip Trip { get; set; } = null!;

    public virtual TransportOrder Order { get; set; } = null!;

    public virtual User ConfirmedByDriver { get; set; } = null!;
}
