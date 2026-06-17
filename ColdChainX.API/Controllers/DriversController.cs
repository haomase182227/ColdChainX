using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;
using FleetCreateDriverRequest = ColdChainX.Application.DTOs.Fleet.CreateDriverRequest;
using ColdChainX.Application.DTOs.Fleet;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _driverService;
        private readonly IFleetManagementService _fleetService;

        public DriversController(IDriverService driverService, IFleetManagementService fleetService)
        {
            _driverService = driverService;
            _fleetService = fleetService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _fleetService.GetDriversAsync();
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var result = await _fleetService.GetDriverByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FleetCreateDriverRequest request)
        {
            var result = await _fleetService.CreateDriverAsync(request);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Import([FromForm] ImportExcelRequest request)
        {
            var result = await _fleetService.ImportDriversAsync(request.ExcelFile);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("{driverId:guid}/licenses")]
        public async Task<IActionResult> CreateLicense(Guid driverId, [FromBody] CreateDriverLicenseRequest request)
        {
            var result = await _fleetService.CreateDriverLicenseAsync(driverId, request);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<ApiResponse<DriverDto>>> Update(Guid id, [FromBody] DriverUpdateRequest request)
        {
            var result = await _driverService.UpdateAsync(id, request);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success && result.Message != null && result.Message.Contains("Invalid driver status"))
                return BadRequest(result);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id)
        {
            var result = await _fleetService.SoftDeleteDriverAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}
