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

    [HttpGet("{receiptId}/pdf")]
    public async Task<IActionResult> GetDiscrepancyPdf(Guid receiptId)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Discrepancy.Queries.GenerateDiscrepancyPdfQuery(receiptId));
            return File(pdfBytes, "application/pdf", $"BienBanBatThuong_{receiptId.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
