using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class ReEvaluateInboundQcRequest
{
    public Guid LpnId { get; set; }

    [FromForm(Name = "Actual_Package_Lines")]
    public string? ActualPackageLinesJson { get; set; }

    public decimal? ActualWeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? Temperature { get; set; }
    public List<IFormFile>? EvidenceImages { get; set; }
}
