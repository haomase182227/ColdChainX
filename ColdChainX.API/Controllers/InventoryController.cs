using ColdChainX.Application.Features.Inventory.Queries;
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
    public async Task<IActionResult> GetInventoryAging()
    {
        var result = await _mediator.Send(new GetInventoryAgingQuery());
        return Ok(result);
    }

    [HttpGet("lpns")]
    public async Task<IActionResult> GetLpns([FromQuery] ColdChainX.Core.Enums.LpnState? status, [FromQuery] string? keyword)
    {
        var query = new ColdChainX.Application.Features.Inventory.Queries.GetLpnListQuery 
        { 
            Status = status, 
            Keyword = keyword 
        };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("lpns/{id}")]
    public async Task<IActionResult> GetLpn(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inventory.Queries.GetLpnDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("lpns/{id}")]
    public async Task<IActionResult> UpdateLpn(Guid id, [FromBody] ColdChainX.Application.Features.Inventory.Commands.UpdateLpnCommand command)
    {
        if (id != command.LpnId) return BadRequest();
        var result = await _mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("lpns/{id}")]
    public async Task<IActionResult> DeleteLpn(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inventory.Commands.DeleteLpnCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
