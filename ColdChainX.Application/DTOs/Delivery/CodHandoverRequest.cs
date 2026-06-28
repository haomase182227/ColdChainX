using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class CodHandoverRequest
{
    [Required]
    public decimal ActualCashReceived { get; set; }

    [Required]
    public decimal ActualQrReceived { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
