using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Request nghiệm thu hàng và xác nhận chữ ký khách tại điểm dừng.
/// Quy trình hiện trường: Khách hàng KCS kiểm tra bằng súng nhiệt/sơ đồ LIFO, ký tay bút mực trực tiếp lên Tờ Vận Đơn / Phiếu Giao Hàng (E-Waybill / Transport Document in từ kho).
/// Sau đó, Tài xế chụp ảnh tờ văn bản đã ký này và gửi qua dạng multipart/form-data để hệ thống hợp thức hóa thành tệp ePOD cho Kế toán đối soát.
/// </summary>
public class HandoverConfirmRequest
{
    public Guid TripId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>
    /// File ảnh chụp Tờ Vận Đơn / Phiếu Giao Hàng (E-Waybill / Transport Document in từ kho) có CHỮ KÝ MỰC TƯƠI thực tế của Khách hàng xác nhận sau khi đồng kiểm KCS.
    /// BẮT BUỘC — Sẽ được tự động upload, niêm phong với tọa độ GPS Check-in và nhiệt độ IoT thực tế để tổng hợp thành tệp ePOD.
    /// </summary>
    [Required]
    public IFormFile SignatureFile { get; set; } = null!;

    /// <summary>
    /// File ảnh tổng thể hiện trường lúc bàn giao hàng (hàng đang dỡ xuống xe / sàn kho bãi) — Tùy chọn.
    /// </summary>
    public IFormFile? HandoverPhotoFile { get; set; }
}
