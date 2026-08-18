using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Orders;

public class CreateOrderPackageVariantRequest
{
    public string? VariantName { get; set; }

    public string PackagingType { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal ExpectedUnitWeightKg { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public List<IFormFile> LegalDocuments { get; set; } = new();

    public List<IFormFile> CargoPhotos { get; set; } = new();
}
