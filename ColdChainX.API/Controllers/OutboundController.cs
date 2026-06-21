using ColdChainX.Application.Features.Outbound.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OutboundController : ControllerBase
{
    private readonly IMediator _mediator;

    public OutboundController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick([FromBody] PickLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPost("load-trip")]
    public async Task<IActionResult> LoadTrip([FromBody] CompleteTripLoadingCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders()
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundOrdersQuery());
        return Ok(result);
    }

    [HttpGet("pick-list/{masterTripId}")]
    public async Task<IActionResult> GetPickList(Guid masterTripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundPickListQuery(masterTripId));
        return Ok(result);
    }
}
