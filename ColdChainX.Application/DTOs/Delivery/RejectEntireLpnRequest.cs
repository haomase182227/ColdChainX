using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// DTO cho API Từ chối toàn bộ kiện hàng (Full LPN Rejection). Khách hàng từ chối nhận toàn bộ lô hàng do sự cố hoặc vi phạm nhiệt độ.
/// </summary>
public class RejectEntireLpnRequest
{
    [Required(ErrorMessage = "Vui lòng chọn chuyến đi (TripId).")]
    public Guid TripId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn khách hàng (CustomerId).")]
    public Guid CustomerId { get; set; }

    public string RejectionReason { get; set; } = "TEMPERATURE_VIOLATION_FULL_REJECT";

    /// <summary>
    /// Cờ xác định có mang toàn bộ hàng lỗi về kho bãi và tạo phiếu hậu cần ngược (InboundReturnSlip) hay không.
    /// Nếu tích chọn (true) -> Tạo phiếu trả về kho cho toàn bộ LPN. Nếu không (false) -> Tiêu hủy/xử lý tại Dock.
    /// </summary>
    public bool IsReturnToWarehouse { get; set; } = true;

    [Required(ErrorMessage = "Vui lòng đính kèm file ảnh chụp minh chứng toàn bộ lô hàng bị sự cố/từ chối tại Dock.")]
    public IFormFile EvidenceImageFile { get; set; } = null!;
}
