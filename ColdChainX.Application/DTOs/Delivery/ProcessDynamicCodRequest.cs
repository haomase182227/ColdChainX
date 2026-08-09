using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class ProcessDynamicCodRequest
{
    [Required(ErrorMessage = "Vui long chon chuyen di (TripId).")]
    public Guid TripId { get; set; }

    [Required(ErrorMessage = "Vui long chon khach hang (CustomerId).")]
    public Guid CustomerId { get; set; }

    [Required(ErrorMessage = "Vui long nhap so luong kien/hop khach tu choi.")]
    public int RejectedQuantity { get; set; } // Số lượng hộp/kiện bị từ chối (ví dụ: LPN có 50 hộp, từ chối 3 hộp thì nhập 3)

    public string RejectionReason { get; set; } = "TEMPERATURE_VIOLATION_OSD";

    public bool IsReturnToWarehouse { get; set; } = false;

    [Required(ErrorMessage = "Vui lòng đính kèm file ảnh chụp minh chứng hàng hỏng tại Dock đồng kiểm.")]
    public IFormFile EvidenceImageFile { get; set; } = null!; // Upload trực tiếp file hình minh chứng duy nhất
}
