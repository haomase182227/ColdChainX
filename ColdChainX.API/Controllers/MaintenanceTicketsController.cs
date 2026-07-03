using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid? vehicleId, [FromQuery] string? status, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _fleetService.GetMaintenanceTicketsAsync(vehicleId, status, pageNumber, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _fleetService.GetMaintenanceTicketByIdAsync(id);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{ticketId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid ticketId, [FromBody] CompleteMaintenanceTicketRequest request)
    {
        var result = await _fleetService.CompleteMaintenanceTicketAsync(ticketId, request);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromQuery] string status)
    {
        var result = await _fleetService.UpdateMaintenanceTicketStatusAsync(id, status);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/documents")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file)
    {
        var result = await _fleetService.UploadMaintenanceTicketDocumentAsync(id, file);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
