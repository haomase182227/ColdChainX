using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class RejectEntireLpnRequest
{
    [Required(ErrorMessage = "Vui lòng chọn chuyến đi (TripId).")]
    public Guid TripId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn khách hàng (CustomerId).")]
    public Guid CustomerId { get; set; }

    public string RejectionReason { get; set; } = "TEMPERATURE_VIOLATION_FULL_REJECT";

    public bool IsReturnToWarehouse { get; set; } = true;

    [Required(ErrorMessage = "Vui lòng đính kèm file ảnh chụp minh chứng toàn bộ lô hàng bị sự cố/từ chối tại Dock.")]
    public IFormFile EvidenceImageFile { get; set; } = null!;
}
