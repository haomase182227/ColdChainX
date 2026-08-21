using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class CheckinDriverRequest
{
    [Required(ErrorMessage = "Vui lòng đính kèm file hình ảnh chụp hiện trường xe đã tới kho/bãi giao hàng.")]
    public IFormFile ProofImageFile { get; set; } = null!;

    [Range(-90, 90)]
    public decimal? Latitude { get; set; }

    [Range(-180, 180)]
    public decimal? Longitude { get; set; }

    public DateTimeOffset? LocationTimestamp { get; set; }

    [Range(0, 10000)]
    public double? AccuracyMeters { get; set; }
}
