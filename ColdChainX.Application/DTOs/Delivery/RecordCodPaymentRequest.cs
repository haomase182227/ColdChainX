using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Request thu tiền COD sau khi đã bàn giao hàng và khách ký nhận.
/// Gửi dưới dạng multipart/form-data.
/// </summary>
public class RecordCodPaymentRequest
{
    /// <summary>Phương thức thanh toán: CASH (tiền mặt) hoặc QR (chuyển khoản).</summary>
    [Required]
    public string PaymentMethod { get; set; } = null!;

    /// <summary>
    /// Số tiền COD tài xế thực tế thu được từ khách.
    /// Phải khớp với CodAmountDue trong Bước 2. Nếu sai sẽ bị từ chối 400.
    /// </summary>
    [Required]
    public decimal CodAmountPaid { get; set; }

    /// <summary>
    /// File ảnh biên lai thu tiền (BẮT BUỘC với CASH, khuyến nghị với QR).
    /// Ví dụ: ảnh chụp màn hình giao dịch thành công, ảnh biên nhận tiền mặt.
    /// </summary>
    public IFormFile? PaymentEvidenceFile { get; set; }
}
