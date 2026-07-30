using System;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class VerifyQrPaymentRequest
{
    public IFormFile? PaymentEvidenceFile { get; set; }
}
