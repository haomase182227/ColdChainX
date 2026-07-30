using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class CheckinDriverRequest
{
    [Required(ErrorMessage = "Vui lòng đính kèm file hình ảnh chụp hiện trường xe đã tới kho/bãi giao hàng.")]
    public IFormFile ProofImageFile { get; set; } = null!;
}
