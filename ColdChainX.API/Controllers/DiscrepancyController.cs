using ColdChainX.Application.Features.Discrepancy.Commands;
using ColdChainX.Application.Features.Discrepancy.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

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

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingDiscrepancies()
    {
        var result = await _mediator.Send(new GetPendingDiscrepanciesQuery());
        return Ok(result);
    }

    [HttpGet("{lpnId:guid}")]
    public async Task<IActionResult> GetDiscrepancyDetail(Guid lpnId)
    {
        var result = await _mediator.Send(new GetDiscrepancyDetailQuery(lpnId));
        if (result == null)
            return NotFound(new { Message = "Discrepancy LPN not found" });

        return Ok(result);
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
