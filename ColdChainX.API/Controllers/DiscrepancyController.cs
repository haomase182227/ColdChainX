using ColdChainX.Application.Features.Discrepancy.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiscrepancyController : ControllerBase
{
    private readonly IMediator _mediator;

    public DiscrepancyController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveDiscrepancy([FromBody] ResolveDiscrepancyCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}
