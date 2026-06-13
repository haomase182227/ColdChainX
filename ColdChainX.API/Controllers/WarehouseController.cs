using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/warehouses")]
    [Authorize]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;

        public WarehouseController(IWarehouseService warehouseService)
        {
            _warehouseService = warehouseService;
        }

        /// <summary>
        /// Create a new warehouse. (Roles: Admin, Manager)
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _warehouseService.CreateAsync(request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse. (Roles: Admin, Manager)
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _warehouseService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse. (Roles: Admin)
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized("User ID claim is missing or invalid in the token.");

            var result = await _warehouseService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse details by its unique identifier. (Roles: All authenticated users)
        /// </summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _warehouseService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of warehouses with optional keyword search. (Roles: All authenticated users)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var result = await _warehouseService.GetListAsync(pageNumber, pageSize, search);
            return Ok(result);
        }
    }
}
