using System;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Kết quả thu tiền COD và hoàn tất quy trình giao nhận.
/// </summary>
public class RecordCodPaymentResponse
{
    /// <summary>ID của ePOD.</summary>
    public Guid EpodId { get; set; }

    /// <summary>Trạng thái thanh toán: PAID hoặc AWAITING_QR (chờ webhook ngân hàng).</summary>
    public string PaymentStatus { get; set; } = null!;

    /// <summary>Thời điểm xác nhận thu tiền (UTC). Null nếu đang chờ QR.</summary>
    public DateTime? PaymentConfirmedAt { get; set; }

    /// <summary>
    /// Link PDF ePOD hoàn chỉnh (có chữ ký + ảnh bằng chứng + nhiệt độ IoT + COD).
    /// Null nếu đang chờ webhook QR — sẽ được sinh sau khi ngân hàng xác nhận.
    /// </summary>
    public string? EpodPdfUrl { get; set; }

    /// <summary>
    /// Bước tiếp theo cần thực hiện.
    /// Ví dụ: "POST /api/stops/{stopId}/departures để xuất phát điểm tiếp theo"
    /// </summary>
    public string NextStep { get; set; } = null!;

    /// <summary>
    /// [QR only] URL ảnh mã QR PayOS để hiển thị trực tiếp trên màn hình driver app.
    /// Null nếu thanh toán CASH hoặc chưa tạo link.
    /// </summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>
    /// [QR only] URL trang thanh toán PayOS (có thể mở trên trình duyệt hoặc webview).
    /// Null nếu thanh toán CASH.
    /// </summary>
    public string? CheckoutUrl { get; set; }
}
