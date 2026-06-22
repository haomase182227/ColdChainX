using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class ReEvaluateInboundQcRequest
{
    public Guid LpnId { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
}
