using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.WarehouseLocation;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Authorize]
    public class WarehouseLocationsController : ControllerBase
    {
        private readonly IWarehouseLocationService _locationService;

        public WarehouseLocationsController(IWarehouseLocationService locationService)
        {
            _locationService = locationService;
        }

        /// <summary>
        /// Create a new location inside a warehouse zone. (Roles: Admin, Manager)
        /// </summary>
        [HttpPost("api/zones/{zoneId:guid}/locations")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create([FromRoute] Guid zoneId, [FromBody] CreateWarehouseLocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _locationService.CreateAsync(zoneId, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse location. (Roles: Admin, Manager)
        /// </summary>
        [HttpPut("api/locations/{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseLocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _locationService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse location. (Roles: Admin)
        /// </summary>
        [HttpDelete("api/locations/{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _locationService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse location details by its unique identifier. (Roles: All authenticated users)
        /// </summary>
        [HttpGet("api/locations/{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _locationService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of locations for a specific zone. (Roles: All authenticated users)
        /// </summary>
        [HttpGet("api/zones/{zoneId:guid}/locations")]
        public async Task<IActionResult> GetList(
            [FromRoute] Guid zoneId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var result = await _locationService.GetListAsync(zoneId, pageNumber, pageSize, search);
            return Ok(result);
        }
    }
}
