using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class RejectLpnDeliveryRequest
{
    [Required]
    [MaxLength(50)]
    public string RejectReason { get; set; } = null!; // "DAMAGED" | "WRONG_ITEM" | "REFUSED_BY_CUSTOMER" | "TEMPERATURE_DEVIATION" | "OTHER"

    [MaxLength(500)]
    public string? RejectNote { get; set; } // required if RejectReason == "OTHER"

    [Required]
    public IFormFile EvidenceImage { get; set; } = null!;
}
