using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.WarehouseZone;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages warehouse zones, representing physical temperature sectors within a warehouse.
    /// </summary>
    [ApiController]
    [Authorize]
    public class WarehouseZonesController : ControllerBase
    {
        private readonly IWarehouseZoneService _zoneService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WarehouseZonesController"/> class.
        /// </summary>
        /// <param name="zoneService">The service used to manage warehouse zones.</param>
        public WarehouseZonesController(IWarehouseZoneService zoneService)
        {
            _zoneService = zoneService;
        }

        /// <summary>
        /// Create a new zone inside a warehouse.
        /// </summary>
        /// <remarks>
        /// Configures a new storage zone within the specified warehouse parent.
        /// 
        /// Business purpose:
        /// Partition a warehouse into specific temperature-controlled ranges (e.g., Deep Freeze, Chilled) or special handling sectors.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Allows location assignment under this zone, enabling proper stock allocation based on temperature needs.
        /// </remarks>
        /// <param name="warehouseId">The unique identifier of the warehouse.</param>
        /// <param name="request">The zone creation parameters.</param>
        /// <returns>The newly created warehouse zone details.</returns>
        [HttpPost("api/v1/warehouses/{warehouseId:guid}/zones")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseZoneResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Create([FromRoute] Guid warehouseId, [FromBody] CreateWarehouseZoneRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _zoneService.CreateAsync(warehouseId, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse zone.
        /// </summary>
        /// <remarks>
        /// Updates settings (like temperature ranges, code, name, capacity) of a zone.
        /// 
        /// Business purpose:
        /// Reconfigure zone specifications to meet changing cold chain regulatory standards or inventory capacity limits.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Alters capacity bounds and temperature validation parameters for any location within this zone.
        /// </remarks>
        /// <param name="id">The unique identifier of the warehouse zone.</param>
        /// <param name="request">The updated zone properties.</param>
        /// <returns>The updated zone details.</returns>
        [HttpPut("api/v1/zones/{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseZoneResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseZoneRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _zoneService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse zone.
        /// </summary>
        /// <remarks>
        /// Deactivates a zone and marks it as deleted in the database.
        /// 
        /// Business purpose:
        /// Retire storage zones that are physically dismantled or no longer in use.
        /// 
        /// Required roles:
        /// Admin
        /// 
        /// Workflow impact:
        /// Makes all associated locations unavailable for future receipt/movement operations.
        /// </remarks>
        /// <param name="id">The unique identifier of the zone to delete.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpDelete("api/v1/zones/{id:guid}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _zoneService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse zone details by its unique identifier.
        /// </summary>
        /// <remarks>
        /// Retrieves the properties of a single zone.
        /// 
        /// Business purpose:
        /// Display current operational status and temperature limits of a zone.
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Read-only lookup for details in UI forms or operational dashboards.
        /// </remarks>
        /// <param name="id">The unique identifier of the warehouse zone.</param>
        /// <returns>The zone details.</returns>
        [HttpGet("api/v1/zones/{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseZoneResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _zoneService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of zones for a specific warehouse.
        /// </summary>
        /// <remarks>
        /// Retrieves a page of zone records associated with a warehouse.
        /// 
        /// Business purpose:
        /// Allow operators to browse zones within a selected warehouse.
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Useful for building tiered dropdown menus in the UI (Warehouse -> Zone -> Location).
        /// </remarks>
        /// <param name="warehouseId">The unique identifier of the parent warehouse.</param>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="search">Optional query string to search zones by name or code.</param>
        /// <returns>A paginated list of warehouse zones.</returns>
        [HttpGet("api/v1/warehouses/{warehouseId:guid}/zones")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<WarehouseZoneResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
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
