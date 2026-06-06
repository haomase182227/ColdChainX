using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
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

        public VehiclesController(IVehicleService vehicleService)
        {
            _vehicleService = vehicleService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<VehicleDto>>>> GetAll()
        {
            var result = await _vehicleService.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ApiResponse<VehicleDto>>> GetById(Guid id)
        {
            var result = await _vehicleService.GetByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<VehicleDto>>> Create([FromBody] VehicleCreateRequest request)
        {
            var result = await _vehicleService.CreateAsync(request);
            if (!result.Success) return Conflict(result);
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
            var result = await _vehicleService.DeleteAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}