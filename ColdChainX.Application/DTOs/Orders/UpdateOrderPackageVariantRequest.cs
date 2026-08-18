using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Orders;

/// <summary>
/// A package size in a full replacement update. Existing IDs are updated,
/// missing existing IDs are deleted, and rows without an ID are created.
/// </summary>
public class UpdateOrderPackageVariantRequest
{
    public Guid? OrderPackageVariantId { get; set; }

    public string? VariantName { get; set; }

    public string PackagingType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal ExpectedUnitWeightKg { get; set; }

    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public List<Guid> RemoveDocumentIds { get; set; } = new();

    public List<IFormFile> LegalDocuments { get; set; } = new();

    public List<IFormFile> CargoPhotos { get; set; } = new();
}
