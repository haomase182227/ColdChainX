using System;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Thông tin từng kiện hàng (LPN) trong quá trình nghiệm thu.
/// </summary>
public class HandoverConfirmLpnInput
{
    /// <summary>ID của kiện hàng cần xác nhận.</summary>
    public Guid LpnId { get; set; }

    /// <summary>true = Khách chấp nhận nhận hàng; false = Khách từ chối.</summary>
    public bool IsAccepted { get; set; }

    /// <summary>Lý do từ chối (bắt buộc khi IsAccepted = false). Ví dụ: DAMAGED, WRONG_ITEM, QUANTITY_MISMATCH.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Ghi chú bổ sung khi từ chối. Ví dụ: "Thùng carton bị móp góc, ướt đáy".</summary>
    public string? RejectionNotes { get; set; }

    /// <summary>
    /// Ảnh bằng chứng hàng lỗi (BẮT BUỘC khi IsAccepted = false).
    /// Upload trực tiếp file ảnh — không cần upload riêng lấy URL.
    /// </summary>
    public IFormFile? EvidencePhotoFile { get; set; }

    /// <summary>Ảnh tình trạng kiện hàng khi nhận (tùy chọn, ngay cả khi chấp nhận).</summary>
    public IFormFile? ConditionPhotoFile { get; set; }

    /// <summary>Đường dẫn ảnh bằng chứng (sử dụng khi đã upload trước lấy URL).</summary>
    public string? EvidenceImageUrl { get; set; }

    /// <summary>Đường dẫn ảnh tình trạng (sử dụng khi đã upload trước lấy URL).</summary>
    public string? ConditionImageUrl { get; set; }
}
