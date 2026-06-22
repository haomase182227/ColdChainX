namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class TripLoadingResponse
{
    public Guid TripId { get; set; }

    public string SealNumber { get; set; } = null!;

    public int ShippedLpnCount { get; set; }

    public string HandoverPdfUrl { get; set; } = null!;

    public string InternalIssuePdfUrl { get; set; } = null!;

    public string ManifestPdfUrl { get; set; } = null!;
}
