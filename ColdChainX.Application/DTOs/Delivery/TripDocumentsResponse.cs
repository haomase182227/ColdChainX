using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Delivery;

public class TripDocumentsResponse
{
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string StopAddress { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string TemperatureRange { get; set; } = string.Empty;
    public int TotalCustomerOrders { get; set; }
    public int TotalDocuments { get; set; }
    public List<ManifestDocumentItem> Documents { get; set; } = new();
}


public class ManifestDocumentItem
{
    public Guid DocId { get; set; }
    public Guid? OrderId { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}
