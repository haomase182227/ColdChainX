using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/maintenance-tickets")]
[Authorize(Roles = "Admin,WarehouseWorker,Dispatcher")]
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
        if (pageNumber <= 0 || pageSize <= 0)
            return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));

        var result = await _fleetService.GetMaintenanceTicketsAsync(vehicleId, status, pageNumber, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _fleetService.GetMaintenanceTicketByIdAsync(id);
        return StatusCode(result.StatusCode != 0 ? result.StatusCode : (result.Success ? 200 : 404), result);
    }

    [HttpPut("{ticketId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid ticketId, [FromBody] CompleteMaintenanceTicketRequest request)
    {
        var result = await _fleetService.CompleteMaintenanceTicketAsync(ticketId, request);
        return StatusCode(result.StatusCode != 0 ? result.StatusCode : (result.Success ? 200 : 400), result);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromQuery] string status)
    {
        var result = await _fleetService.UpdateMaintenanceTicketStatusAsync(id, status);
        return StatusCode(result.StatusCode != 0 ? result.StatusCode : (result.Success ? 200 : 400), result);
    }

    [HttpPost("{id:guid}/documents")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file)
    {
        var result = await _fleetService.UploadMaintenanceTicketDocumentAsync(id, file);
        return StatusCode(result.StatusCode != 0 ? result.StatusCode : (result.Success ? 200 : 400), result);
    }
}
