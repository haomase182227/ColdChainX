using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/vehicles")]
    [Authorize]
    public class VehiclesController : ControllerBase
    {
        private readonly IVehicleService _vehicleService;
        private readonly IFleetManagementService _fleetService;
        private readonly IFileService _fileService;

        public VehiclesController(IVehicleService vehicleService, IFleetManagementService fleetService, IFileService fileService)
        {
            _vehicleService = vehicleService;
            _fleetService = fleetService;
            _fileService = fileService;
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
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        public async Task<IActionResult> Create([FromBody] CreateVehicleRequest request)
        {
            var result = await _fleetService.CreateVehicleAsync(request);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
        {
            var result = await _fleetService.ImportVehiclesAsync(request.ExcelFile);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("{vehicleId:guid}/documents")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        public async Task<IActionResult> CreateDocument(Guid vehicleId, [FromBody] CreateVehicleDocumentRequest request)
        {
            var result = await _fleetService.CreateVehicleDocumentAsync(vehicleId, request);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("sync-odometer")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER,Driver,DRIVER")]
        public async Task<IActionResult> SyncOdometer([FromForm] SyncOdometerRequest request)
        {
            Guid? userId = null;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            string? photoUrl = null;
            if (request.OdometerPhoto != null)
            {
                photoUrl = await _fileService.UploadFileAsync(request.OdometerPhoto);
            }

            var result = await _fleetService.SyncOdometerAsync(request, userId, photoUrl);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost("{vehicleId:guid}/maintenance-tickets")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
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
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        public async Task<IActionResult> Update(Guid id, [FromBody] VehicleUpdateRequest request)
        {
            var result = await _fleetService.UpdateVehicleAsync(id, request);
            if (!result.Success && result.Message == "Vehicle not found") return NotFound(result);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
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
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
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
