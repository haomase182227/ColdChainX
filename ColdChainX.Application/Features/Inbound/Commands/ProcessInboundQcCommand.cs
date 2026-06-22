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
}

public class ProcessInboundQcRequest
{
    public Guid AsnId { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
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
