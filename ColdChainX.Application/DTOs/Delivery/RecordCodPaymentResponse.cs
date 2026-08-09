using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class RecordCodPaymentResponse
{
    public Guid EpodId { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public DateTime? PaymentConfirmedAt { get; set; }

    public string? EpodPdfUrl { get; set; }

    public string NextStep { get; set; } = null!;

    public string? QrCodeUrl { get; set; }

    public string? CheckoutUrl { get; set; }
}
