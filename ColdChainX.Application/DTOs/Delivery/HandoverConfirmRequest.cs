using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Request nghiệm thu hàng và xác nhận chữ ký khách tại điểm dừng.
/// Gửi dưới dạng multipart/form-data để upload ảnh chữ ký và ảnh bằng chứng trực tiếp.
/// </summary>
public class HandoverConfirmRequest
{
    /// <summary>ID Đơn hàng cần nghiệm thu.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Tên người nhận hàng.</summary>
    [Required]
    [MaxLength(100)]
    public string ReceiverName { get; set; } = null!;

    /// <summary>Số điện thoại người nhận (tùy chọn).</summary>
    [MaxLength(20)]
    public string? ReceiverPhone { get; set; }

    /// <summary>
    /// File ảnh chữ ký tay của khách hàng ký trên màn hình.
    /// BẮT BUỘC — Sẽ được upload lên Cloudinary tự động.
    /// </summary>
    [Required]
    public IFormFile SignatureFile { get; set; } = null!;

    /// <summary>
    /// File ảnh tổng thể lúc bàn giao hàng (hàng đang trên xe/sàn nhà) — Tùy chọn.
    /// </summary>
    public IFormFile? HandoverPhotoFile { get; set; }

    /// <summary>Đánh giá chất lượng dịch vụ giao hàng (1–5 sao, mặc định 5).</summary>
    public int DeliveryRating { get; set; } = 5;

    /// <summary>Ghi chú thêm của tài xế hoặc khách hàng.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Danh sách trạng thái từng kiện hàng (LPN).
    /// Nếu để trống, hệ thống tự động chấp nhận toàn bộ LPN (auto-accept all).
    /// </summary>
    public List<HandoverConfirmLpnInput> Lpns { get; set; } = new();
}
