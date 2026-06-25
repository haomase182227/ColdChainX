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
}
