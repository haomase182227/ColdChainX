using System;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class HandoverConfirmLpnInput
{
    public Guid LpnId { get; set; }

    public bool IsAccepted { get; set; }

    public string? RejectionReason { get; set; }

    public string? RejectionNotes { get; set; }

    public IFormFile? EvidencePhotoFile { get; set; }

    public IFormFile? ConditionPhotoFile { get; set; }

    public string? EvidenceImageUrl { get; set; }

    public string? ConditionImageUrl { get; set; }
}
