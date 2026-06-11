using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly IDispatchService _dispatchService;

    public DispatchController(IDispatchService dispatchService)
    {
        _dispatchService = dispatchService;
    }

    [HttpPost("plan-load")]
    public async Task<IActionResult> PlanLoad([FromBody] PlanLoadRequest request)
    {
        try
        {
            var plan = await _dispatchService.SuggestLoadPlanAsync(request.OrderIds, request.VehicleId);
            return Ok(new { Plan = plan });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = "Internal server error.", Details = ex.Message });
        }
    }

    [HttpPost("route-lifo/{tripId}")]
    public async Task<IActionResult> CalculateRouteAndLIFO(Guid tripId)
    {
        try
        {
            await _dispatchService.CalculateRouteAndLIFOAsync(tripId);
            return Ok(new { Message = "Route calculated and LIFO stops planned." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("seal/{tripId}")]
    public async Task<IActionResult> SealTruck(Guid tripId, [FromBody] SealRequest request)
    {
        try
        {
            // Dummy keeper ID for now
            await _dispatchService.SealTruckAsync(tripId, request.SealCode, Guid.NewGuid());
            return Ok(new { Message = "Truck sealed successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("issue-documents/{tripId}")]
    public async Task<IActionResult> IssueDocuments(Guid tripId)
    {
        try
        {
            await _dispatchService.IssueDispatchDocumentsAsync(tripId);
            return Ok(new { Message = "Dispatch documents issued." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

public class PlanLoadRequest
{
    public List<Guid> OrderIds { get; set; } = new();
    public Guid VehicleId { get; set; }
}

public class SealRequest
{
    public string SealCode { get; set; } = null!;
}
