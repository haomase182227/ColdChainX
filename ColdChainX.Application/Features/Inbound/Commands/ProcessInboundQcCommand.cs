using MediatR;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    public decimal? ActualWeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public string? ActualPackageLinesJson { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
}

public class ProcessInboundQcRequest
{
    [FromForm(Name = "Asn_ID")]
    public Guid? AsnId { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public Guid? WarehouseId { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public decimal? ActualWeightKg { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public decimal? LengthCm { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public decimal? WidthCm { get; set; }

    [JsonIgnore]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public decimal? HeightCm { get; set; }

    [FromForm(Name = "Actual_Package_Lines")]
    public string? ActualPackageLinesJson { get; set; }

    [FromForm(Name = "Temperature")]
    public decimal? Temperature { get; set; }

    [FromForm(Name = "Evidence_Images")]
    public List<IFormFile>? EvidenceImages { get; set; }
}

public class InboundQcPackageLineRequest
{
    public string? Label { get; set; }
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
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
}
