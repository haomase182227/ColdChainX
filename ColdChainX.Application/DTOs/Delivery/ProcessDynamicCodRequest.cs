using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// DTO cho API Đồng kiểm OS&D ngang tầm với confirm-handover (đối chiếu bằng TripId & CustomerId, có cờ IsReturnToWarehouse quyết định trả hàng về kho).
/// </summary>
public class ProcessDynamicCodRequest
{
    [Required(ErrorMessage = "Vui lòng chọn chuyến đi (TripId).")]
    public Guid TripId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn khách hàng (CustomerId).")]
    public Guid CustomerId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số lượng kiện/hộp khách từ chối (bị hư hỏng hoặc vi phạm nhiệt độ).")]
    public int RejectedQuantity { get; set; } // Số lượng hộp/kiện bị từ chối (ví dụ: LPN có 50 hộp, từ chối 3 hộp thì nhập 3)

    public string RejectionReason { get; set; } = "TEMPERATURE_VIOLATION_OSD";

    /// <summary>
    /// Cờ xác định có mang hàng lỗi về kho bãi và tạo phiếu hậu cần ngược (InboundReturnSlip) hay không.
    /// Nếu tích chọn (true) -> Tạo phiếu trả về kho. Nếu không (false) -> Tiêu hủy hoặc xử lý hiện trường, không mang về kho.
    /// </summary>
    public bool IsReturnToWarehouse { get; set; } = false;

    [Required(ErrorMessage = "Vui lòng đính kèm file ảnh chụp minh chứng hàng hỏng tại Dock đồng kiểm.")]
    public IFormFile EvidenceImageFile { get; set; } = null!; // Upload trực tiếp file hình minh chứng duy nhất
}
