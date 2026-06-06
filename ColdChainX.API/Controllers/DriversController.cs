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
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverService _driverService;

        public DriversController(IDriverService driverService)
        {
            _driverService = driverService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DriverDto>>>> GetAll()
        {
            var result = await _driverService.GetAllAsync();
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<ApiResponse<DriverDto>>> GetById(Guid id)
        {
            var result = await _driverService.GetByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<DriverDto>>> Create([FromBody] DriverCreateRequest request)
        {
            var result = await _driverService.CreateAsync(request);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<ApiResponse<DriverDto>>> Update(Guid id, [FromBody] DriverUpdateRequest request)
        {
            var result = await _driverService.UpdateAsync(id, request);
            if (!result.Success && result.Message == "Driver not found") return NotFound(result);
            if (!result.Success) return Conflict(result);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id)
        {
            var result = await _driverService.DeleteAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }
    }
}