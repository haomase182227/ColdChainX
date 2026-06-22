using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/v1/inventory/lpns")]
[Authorize]
public class InventoryAgingController : ControllerBase
{
    private readonly IWarehouseFlowService _warehouseFlowService;

    public InventoryAgingController(IWarehouseFlowService warehouseFlowService)
    {
        _warehouseFlowService = warehouseFlowService;
    }

    [HttpGet("aging")]
    public async Task<IActionResult> GetInventoryAging([FromQuery] string? state = null, [FromQuery] string? storageLocation = null)
    {
        var result = await _warehouseFlowService.GetInventoryAgingAsync(state, storageLocation);
        return Ok(result);
    }
}
