using System;

namespace ColdChainX.Application.DTOs.Claim;

public class CompleteClaimPayoutRequest
{
    public string TransactionRef { get; set; } = null!;
    public string PaymentMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}
