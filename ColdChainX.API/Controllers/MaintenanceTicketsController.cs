using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/maintenance-tickets")]
public class MaintenanceTicketsController : ControllerBase
{
    private readonly IFleetManagementService _fleetService;

    public MaintenanceTicketsController(IFleetManagementService fleetService)
    {
        _fleetService = fleetService;
    }

    [HttpPut("{ticketId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid ticketId, [FromBody] CompleteMaintenanceTicketRequest request)
    {
        var result = await _fleetService.CompleteMaintenanceTicketAsync(ticketId, request);
        return result.Success ? Ok(result) : NotFound(result);
    }
}
