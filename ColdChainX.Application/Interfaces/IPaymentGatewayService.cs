namespace ColdChainX.Application.Interfaces;

public interface IPaymentGatewayService
{
    Task<CreateQrResult> CreatePaymentLinkAsync(
        long orderCode,
        int amount,
        string description,
        CancellationToken cancellationToken = default);

    bool VerifyWebhookSignature(string webhookBody, string signature);
}

public class CreateQrResult
{
    public string CheckoutUrl { get; set; } = null!;

    public string QrCodeUrl { get; set; } = null!;

    public long OrderCode { get; set; }
}
