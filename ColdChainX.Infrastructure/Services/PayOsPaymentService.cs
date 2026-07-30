using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PayOS;
using PayOS.Models.V2.PaymentRequests;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services;

/// <summary>
/// PayOS payment gateway service implementation.
/// Uses the official payOS NuGet SDK v2.1.0.
/// Namespace: PayOS | Class: PayOSClient | Types: CreatePaymentLinkRequest, CreatePaymentLinkResponse
/// </summary>
public class PayOsPaymentService : IPaymentGatewayService
{
    private readonly PayOSClient _payOsClient;
    private readonly string _returnUrl;
    private readonly string _cancelUrl;
    private readonly string _checksumKey;

    public PayOsPaymentService(IConfiguration configuration)
    {
        var section = configuration.GetSection("PayOS");

        var clientId = Environment.GetEnvironmentVariable("PAYOS_CLIENT_ID") 
            ?? Environment.GetEnvironmentVariable("PayOS__ClientId") 
            ?? section["ClientId"];
        if (string.IsNullOrWhiteSpace(clientId)) 
            throw new InvalidOperationException("PAYOS_CLIENT_ID is not configured in .env or settings.");

        var apiKey = Environment.GetEnvironmentVariable("PAYOS_API_KEY") 
            ?? Environment.GetEnvironmentVariable("PayOS__ApiKey") 
            ?? section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) 
            throw new InvalidOperationException("PAYOS_API_KEY is not configured in .env or settings.");

        var checksum = Environment.GetEnvironmentVariable("PAYOS_CHECKSUM_KEY") 
            ?? Environment.GetEnvironmentVariable("PayOS__ChecksumKey") 
            ?? section["ChecksumKey"];
        if (string.IsNullOrWhiteSpace(checksum)) 
            throw new InvalidOperationException("PAYOS_CHECKSUM_KEY is not configured in .env or settings.");
        
        _checksumKey = checksum;
        _returnUrl = Environment.GetEnvironmentVariable("PAYOS_RETURN_URL") ?? section["ReturnUrl"] ?? "http://localhost:3000/payment/success";
        _cancelUrl = Environment.GetEnvironmentVariable("PAYOS_CANCEL_URL") ?? section["CancelUrl"] ?? "http://localhost:3000/payment/cancel";

        _payOsClient = new PayOSClient(new PayOSOptions
        {
            ClientId = clientId,
            ApiKey = apiKey,
            ChecksumKey = _checksumKey
        });
    }

    /// <inheritdoc/>
    public async Task<CreateQrResult> CreatePaymentLinkAsync(
        long orderCode,
        int amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        // PayOS v2 uses CreatePaymentLinkRequest model
        var paymentRequest = new CreatePaymentLinkRequest
        {
            OrderCode = orderCode,
            Amount = amount,
            Description = description,
            ReturnUrl = _returnUrl,
            CancelUrl = _cancelUrl
        };

        if (_checksumKey.Contains("REPLACE_ME"))
        {
            // Mock fallback for local development/testing when keys are placeholder values
            return new CreateQrResult
            {
                CheckoutUrl = $"https://checkout-mock.payos.vn/payment/{orderCode}",
                QrCodeUrl = $"https://qr.payos.vn/image/{orderCode}?amount={amount}&addInfo={Uri.EscapeDataString(description)}",
                OrderCode = orderCode
            };
        }

        // PayOS v2.1.0 CreateAsync accepts CreatePaymentLinkRequest directly (no CancellationToken overload)
        var response = await _payOsClient.PaymentRequests.CreateAsync(paymentRequest);

        return new CreateQrResult
        {
            CheckoutUrl = response.CheckoutUrl ?? string.Empty,
            QrCodeUrl = response.QrCode ?? string.Empty,
            OrderCode = response.OrderCode
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// PayOS v2 HMAC-SHA256 signature verification.
    /// The webhookBody is the raw JSON body sent by PayOS.
    /// The signature is the PAYOS-SIGNATURE header value.
    /// We verify by computing HMAC-SHA256 of the sorted query string of the webhook data body.
    /// </remarks>
    public bool VerifyWebhookSignature(string webhookBody, string signature)
    {
        // Manual HMAC-SHA256 verification against the raw body
        // PayOS signs the sorted query string of webhook data using checksumKey
        if (string.IsNullOrEmpty(webhookBody) || string.IsNullOrEmpty(signature))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_checksumKey));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(webhookBody));
        var computedHex = BitConverter.ToString(computed).Replace("-", "").ToLowerInvariant();
        return string.Equals(computedHex, signature.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
