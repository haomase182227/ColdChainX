using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class ConfirmLpnDeliveryRequest
{
    [Required]
    [MaxLength(200)]
    public string ReceiverName { get; set; } = null!;

    [MaxLength(20)]
    public string? ReceiverPhone { get; set; }

    [Required]
    public IFormFile EvidenceImage { get; set; } = null!;

    public DateTime? CheckinAt { get; set; }

    public IFormFile? SignatureImage { get; set; }

    public decimal CodAmount { get; set; }

    public string? CodPaymentMethod { get; set; }

    public IFormFile? CodReceiptImage { get; set; }

    public string? NewSealNumber { get; set; }
}
