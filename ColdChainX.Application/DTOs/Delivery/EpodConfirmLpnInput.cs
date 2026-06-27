using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class EpodConfirmLpnInput
{
    public Guid LpnId { get; set; }
    public bool IsAccepted { get; set; }
    public string? RejectionReason { get; set; }
    public string? RejectionNotes { get; set; }
    public string? EvidenceImageUrl { get; set; }
}
