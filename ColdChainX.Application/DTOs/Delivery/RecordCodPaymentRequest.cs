using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class RecordCodPaymentRequest
{
    [Required]
    public string PaymentMethod { get; set; } = null!;

    [Required]
    public decimal CodAmountPaid { get; set; }

    public IFormFile? PaymentEvidenceFile { get; set; }
}
