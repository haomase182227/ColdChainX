using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.WarehouseZone;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Authorize]
    public class WarehouseZonesController : ControllerBase
    {
        private readonly IWarehouseZoneService _zoneService;

        public WarehouseZonesController(IWarehouseZoneService zoneService)
        {
            _zoneService = zoneService;
        }

        /// <summary>
        /// Create a new zone inside a warehouse. (Roles: Admin, Manager)
        /// </summary>
        [HttpPost("api/warehouses/{warehouseId:guid}/zones")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create([FromRoute] Guid warehouseId, [FromBody] CreateWarehouseZoneRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _zoneService.CreateAsync(warehouseId, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse zone. (Roles: Admin, Manager)
        /// </summary>
        [HttpPut("api/zones/{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseZoneRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _zoneService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse zone. (Roles: Admin)
        /// </summary>
        [HttpDelete("api/zones/{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _zoneService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse zone details by its unique identifier. (Roles: All authenticated users)
        /// </summary>
        [HttpGet("api/zones/{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _zoneService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of zones for a specific warehouse. (Roles: All authenticated users)
        /// </summary>
        [HttpGet("api/warehouses/{warehouseId:guid}/zones")]
        public async Task<IActionResult> GetList(
            [FromRoute] Guid warehouseId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var result = await _zoneService.GetListAsync(warehouseId, pageNumber, pageSize, search);
            return Ok(result);
        }
    }
}
