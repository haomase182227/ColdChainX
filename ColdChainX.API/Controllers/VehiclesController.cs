using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/vehicles")]
    public class VehiclesController : ControllerBase
    {
        private readonly IVehicleService _vehicleService;
        private readonly IFleetManagementService _fleetService;

        public VehiclesController(IVehicleService vehicleService, IFleetManagementService fleetService)
        {
            _vehicleService = vehicleService;
            _fleetService = fleetService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _fleetService.GetVehiclesAsync();
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _fleetService.GetVehicleByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVehicleRequest request)
        {
            var result = await _fleetService.CreateVehicleAsync(request);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
        {
            var result = await _fleetService.ImportVehiclesAsync(request.ExcelFile);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("{vehicleId:guid}/documents")]
        public async Task<IActionResult> CreateDocument(Guid vehicleId, [FromBody] CreateVehicleDocumentRequest request)
        {
            var result = await _fleetService.CreateVehicleDocumentAsync(vehicleId, request);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("{truckPlate}/sync-odometer")]
        public async Task<IActionResult> SyncOdometer(string truckPlate, [FromBody] SyncOdometerRequest request)
        {
            Guid? userId = null;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var result = await _fleetService.SyncOdometerAsync(truckPlate, request, userId);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("{vehicleId:guid}/maintenance-tickets")]
        public async Task<IActionResult> CreateMaintenanceTicket(Guid vehicleId, [FromBody] CreateMaintenanceTicketRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("UserId claim is missing from token");

            var result = await _fleetService.CreateMaintenanceTicketAsync(vehicleId, request, userId);
            if (!result.Success && result.Message == "Vehicle not found") return NotFound(result);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<ApiResponse<VehicleDto>>> Update(Guid id, [FromBody] VehicleUpdateRequest request)
        {
            var result = await _vehicleService.UpdateAsync(id, request);
            if (!result.Success && result.Message == "Vehicle not found") return NotFound(result);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id)
        {
            var result = await _fleetService.SoftDeleteVehicleAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpGet("{id:guid}/maintenance-history")]
        public async Task<IActionResult> GetMaintenanceHistory(Guid id)
        {
            var result = await _fleetService.GetVehicleMaintenanceHistoryAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("{id:guid}/mark-unavailable")]
        public async Task<IActionResult> MarkUnavailable(Guid id, [FromQuery] string reason = "Manual lock")
        {
            var result = await _fleetService.MarkVehicleUnavailableAsync(id, reason);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpGet("{id:guid}/maintenance-forecast")]
        public async Task<IActionResult> GetMaintenanceForecast(Guid id, [FromQuery] Guid? tripId = null)
        {
            var result = await _fleetService.GetVehicleMaintenanceForecastAsync(id, tripId);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}
