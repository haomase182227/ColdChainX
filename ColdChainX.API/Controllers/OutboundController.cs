using ColdChainX.API.Authorization;
using ColdChainX.Application.Features.Outbound.Commands;
using ColdChainX.Shared.Constants;
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
    [HasPermission(PermissionCodes.WarehouseLoadingConfirm)]
    public async Task<IActionResult> Pick([FromBody] PickLpnCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPost("load-trip")]
    [HasPermission(PermissionCodes.WarehouseLoadingConfirm)]
    public async Task<IActionResult> LoadTrip([FromBody] CompleteTripLoadingCommand command)
    {
        var result = await _mediator.Send(command);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("available-lpns")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetAvailableLpns([FromQuery] Guid? tripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetAvailableLpnsQuery(tripId));
        return Ok(result);
    }

    [HttpGet("available-trips")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetAvailableTrips([FromQuery] Guid? tripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetAvailableTripsQuery(tripId));
        return Ok(result);
    }

    [HttpGet("orders")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetOrders([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundOrdersQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("pick-list/{masterTripId}")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetPickList(Guid masterTripId)
    {
        var result = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GetOutboundPickListQuery(masterTripId));
        return Ok(result);
    }

    [HttpGet("orders/{orderId}/epod-pdf")]
    [HasPermission(PermissionCodes.WarehouseTaskView)]
    public async Task<IActionResult> GetEpodPdf(Guid orderId)
    {
        try
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Outbound.Queries.GenerateEpodPdfQuery(orderId));
            return File(pdfBytes, "application/pdf", $"ePOD_{orderId.ToString().Substring(0, 8)}.pdf");
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
