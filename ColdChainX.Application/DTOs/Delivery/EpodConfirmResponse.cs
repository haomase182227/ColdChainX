using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class EpodConfirmResponse
{
    public Guid EpodId { get; set; }
    public string OrderStatus { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;
    public string PdfUrl { get; set; } = null!;
}
