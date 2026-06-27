using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class EpodConfirmRequest
{
    public Guid OrderId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ReceiverName { get; set; } = null!;

    [MaxLength(20)]
    public string? ReceiverPhone { get; set; }

    [Required]
    public string SignImageUrl { get; set; } = null!;

    [Required]
    public decimal SignLatitude { get; set; }

    [Required]
    public decimal SignLongitude { get; set; }

    public int DeliveryRating { get; set; } = 5;

    [MaxLength(500)]
    public string? Note { get; set; }

    public decimal? CodAmountPaid { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = null!; // CASH or QR

    public string? PaymentEvidenceImageUrl { get; set; }

    [Required]
    public List<EpodConfirmLpnInput> Lpns { get; set; } = new();
}
