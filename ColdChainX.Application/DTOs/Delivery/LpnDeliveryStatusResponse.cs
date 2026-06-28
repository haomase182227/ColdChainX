using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class LpnDeliveryStatusResponse
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = null!;
    public string State { get; set; } = null!; // LPN current state
    public string? OutcomeType { get; set; } // "DELIVERED" | "REJECTED"
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? RejectReason { get; set; }
    public string? RejectNote { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CheckinAt { get; set; }
    public string? SignatureImageUrl { get; set; }
    public decimal CodAmount { get; set; }
    public string? CodPaymentMethod { get; set; }
    public string? CodReceiptImageUrl { get; set; }
    public string? NewSealNumber { get; set; }
    public string? VietQrUrl { get; set; }
    public bool IsCodVerified { get; set; }
    public DateTime? CodVerifiedAt { get; set; }
    public decimal? RecordedTemperature { get; set; }
}
