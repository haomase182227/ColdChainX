using ColdChainX.Application.Features.Inbound.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InboundController : ControllerBase
{
    private readonly IMediator _mediator;

    public InboundController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("qc")]
    public async Task<IActionResult> ProcessQc([FromBody] ProcessInboundQcCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);
            
        return Ok(result);
    }

    [HttpPost("putaway")]
    public async Task<IActionResult> Putaway([FromBody] PutawayLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts()
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptsQuery());
        return Ok(result);
    }

    [HttpGet("receipts/{id}")]
    public async Task<IActionResult> GetReceipt(Guid id)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Inbound.Queries.GetInboundReceiptDetailQuery(id));
        if (result == null) return NotFound();
        return Ok(result);
    }
}
