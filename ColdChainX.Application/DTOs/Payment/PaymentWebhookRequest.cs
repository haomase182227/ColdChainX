using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Payment;

public class PaymentWebhookRequest
{
    [Required]
    public string OrderCode { get; set; } = null!;

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public string TransactionId { get; set; } = null!;

    [Required]
    public string Status { get; set; } = null!; // PAID
}
