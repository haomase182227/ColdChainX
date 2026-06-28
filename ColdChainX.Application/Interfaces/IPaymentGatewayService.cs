namespace ColdChainX.Application.Interfaces;

/// <summary>
/// Abstraction over payment gateway (PayOS) for creating QR payment links
/// and verifying incoming webhooks.
/// </summary>
public interface IPaymentGatewayService
{
    /// <summary>
    /// Tạo link thanh toán QR trên PayOS.
    /// </summary>
    /// <param name="orderCode">Mã đơn hàng dạng số (long) — PayOS yêu cầu số nguyên dương.</param>
    /// <param name="amount">Số tiền cần thanh toán (VND, nguyên).</param>
    /// <param name="description">Mô tả giao dịch, tối đa 25 ký tự.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="CreateQrResult"/> chứa URL trang thanh toán PayOS và URL ảnh QR code.
    /// </returns>
    Task<CreateQrResult> CreatePaymentLinkAsync(
        long orderCode,
        int amount,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác thực chữ ký HMAC của webhook payload từ PayOS.
    /// Trả về true nếu chữ ký hợp lệ, false nếu không.
    /// </summary>
    bool VerifyWebhookSignature(string webhookBody, string signature);
}

/// <summary>Kết quả tạo link thanh toán QR từ PayOS.</summary>
public class CreateQrResult
{
    /// <summary>URL trang thanh toán PayOS mà driver/khách mở trên trình duyệt.</summary>
    public string CheckoutUrl { get; set; } = null!;

    /// <summary>URL ảnh QR code để hiển thị trực tiếp trên màn hình driver app.</summary>
    public string QrCodeUrl { get; set; } = null!;

    /// <summary>Mã đơn hàng trên PayOS (dạng số).</summary>
    public long OrderCode { get; set; }
}
