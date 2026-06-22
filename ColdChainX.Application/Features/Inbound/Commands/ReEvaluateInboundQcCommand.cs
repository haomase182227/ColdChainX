using MediatR;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ReEvaluateInboundQcCommand : IRequest<ReEvaluateInboundQcResponse>
{
    public Guid LpnId { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
    public Guid WarehouseId { get; set; }
}

public class ReEvaluateInboundQcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? LpnId { get; set; }
    public string? LpnCode { get; set; }
    public string? State { get; set; }
    public decimal DiffPercent { get; set; }
    public string? PdfUrl { get; set; }
}
