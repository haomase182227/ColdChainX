using ColdChainX.API.Authorization;
using ColdChainX.Application.Features.Inventory.Queries;
using ColdChainX.Shared.Constants;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("aging")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetInventoryAging()
    {
        var result = await _mediator.Send(new GetInventoryAgingQuery());
        return Ok(result);
    }

    [HttpGet("lpns")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetLpns([FromQuery] ColdChainX.Core.Enums.LpnState? status, [FromQuery] string? keyword, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var query = new ColdChainX.Application.Features.Inventory.Queries.GetLpnListQuery 
        { 
            Status = status, 
            Keyword = keyword,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("lpns/{id}")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetLpn(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inventory.Queries.GetLpnDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("lpns/{id}/documents")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetLpnDocuments(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inventory.Queries.GetLpnDocumentsQuery(id));
        if (result == null) return NotFound(new { Success = false, Message = "LPN not found." });
        return Ok(new { Success = true, Data = result });
    }

    [HttpPut("lpns/{id}")]
    [HasPermission(PermissionCodes.WarehouseInventoryAdjust)]
    public async Task<IActionResult> UpdateLpn(Guid id, [FromBody] ColdChainX.Application.Features.Inventory.Commands.UpdateLpnCommand command)
    {
        if (id != command.LpnId) return BadRequest();
        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("lpns/{id}")]
    [HasPermission(PermissionCodes.WarehouseInventoryAdjust)]
    [ProducesResponseType(typeof(ColdChainX.Application.Features.Inventory.Commands.DeleteLpnResponse), 200)]
    [ProducesResponseType(typeof(ColdChainX.Application.Features.Inventory.Commands.DeleteLpnResponse), 400)]
    public async Task<IActionResult> DeleteLpn(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inventory.Commands.DeleteLpnCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
