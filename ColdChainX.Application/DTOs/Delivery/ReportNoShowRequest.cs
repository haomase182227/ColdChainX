using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class ReportNoShowRequest
{
    [Required(ErrorMessage = "Vui lòng đính kèm file hình ảnh bằng chứng khách hàng không xuất hiện / từ chối nhận hàng.")]
    public IFormFile EvidenceImageFile { get; set; } = null!;
}
