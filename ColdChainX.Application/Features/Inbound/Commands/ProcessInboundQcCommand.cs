using MediatR;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundQcCommand : IRequest<ProcessInboundQcResponse>
{
    public Guid AsnId { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Guid WarehouseId { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Guid ReceiverId { get; set; }

    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
    public List<PackageVariantQcMeasurement> PackageMeasurements { get; set; } = new();
}

public class ProcessInboundQcRequest
{
    public Guid AsnId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
    public List<PackageVariantQcMeasurement> PackageMeasurements { get; set; } = new();
}

public class PackageVariantQcMeasurement
{
    public Guid OrderPackageVariantId { get; set; }
    /// <summary>Rows with the same key are packed into one LPN. Defaults to LPN-1.</summary>
    public string LpnGroupKey { get; set; } = "LPN-1";
    /// <summary>Allows a package size to be split across several LPN groups.</summary>
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile> EvidenceImages { get; set; } = new();
}

public class ProcessInboundQcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? LpnId { get; set; }
    public string? LpnCode { get; set; }
    public string? State { get; set; }
    public Guid? ReceiptId { get; set; }

    public decimal DiffPercent { get; set; }
    public string? PdfUrl { get; set; }
    public List<ProcessInboundQcItemResponse> Lpns { get; set; } = new();
}

public class ProcessInboundQcItemResponse
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal DiffPercent { get; set; }
    public List<ProcessInboundQcPackageLineResponse> PackageLines { get; set; } = new();
}

public class ProcessInboundQcPackageLineResponse
{
    public Guid LpnPackageVariantLineId { get; set; }
    public Guid? OrderPackageVariantId { get; set; }
    public string? PackageVariantName { get; set; }
    public string PackingType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal DiffPercent { get; set; }
    public bool HasDiscrepancy { get; set; }
}
