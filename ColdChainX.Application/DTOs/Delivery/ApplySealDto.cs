using System;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class ApplySealRequest
{
    [Required(ErrorMessage = "Mã kẹp chì (SealCode) là bắt buộc.")]
    public string SealCode { get; set; } = string.Empty;
}

public class ApplySealResponse
{
    public Guid SealId { get; set; }
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string SealCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public bool AiAlertingRestored { get; set; }
    public int AiMutedBufferMinutes { get; set; }
    public string AiMonitoringStatus { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
