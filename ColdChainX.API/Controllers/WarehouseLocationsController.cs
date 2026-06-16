using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.WarehouseLocation;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages specific storage locations (racks, bays, levels) within warehouse zones.
    /// </summary>
    [ApiController]
    [Authorize]
    public class WarehouseLocationsController : ControllerBase
    {
        private readonly IWarehouseLocationService _locationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WarehouseLocationsController"/> class.
        /// </summary>
        /// <param name="locationService">The service used to manage warehouse locations.</param>
        public WarehouseLocationsController(IWarehouseLocationService locationService)
        {
            _locationService = locationService;
        }

        /// <summary>
        /// Create a new location inside a warehouse zone.
        /// </summary>
        /// <remarks>
        /// Configures a specific storage coordinate (rack, bay, level) inside a warehouse zone.
        /// 
        /// Business purpose:
        /// Define exact locations where pallets can be put away, tracked, and picked.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Provides target storage addresses for the putaway suggestions algorithm.
        /// </remarks>
        /// <param name="zoneId">The unique identifier of the warehouse zone.</param>
        /// <param name="request">The location details (rack, bay, level, capacity).</param>
        /// <returns>The newly created location details.</returns>
        [HttpPost("api/zones/{zoneId:guid}/locations")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseLocationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Create([FromRoute] Guid zoneId, [FromBody] CreateWarehouseLocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _locationService.CreateAsync(zoneId, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse location.
        /// </summary>
        /// <remarks>
        /// Modifies the details or status of an existing storage location.
        /// 
        /// Business purpose:
        /// Update capacities or mark locations as blocked/damaged when physical maintenance is needed.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Changing status to DAMAGED or INACTIVE will prevent the system from suggesting this location for putaway.
        /// </remarks>
        /// <param name="id">The unique identifier of the location to update.</param>
        /// <param name="request">The updated location details.</param>
        /// <returns>The updated location details.</returns>
        [HttpPut("api/locations/{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseLocationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseLocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _locationService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse location.
        /// </summary>
        /// <remarks>
        /// Marks a warehouse location as deleted.
        /// 
        /// Business purpose:
        /// Remove old or retired storage coordinate definitions from the system.
        /// 
        /// Required roles:
        /// Admin
        /// 
        /// Workflow impact:
        /// Removes the location from active layout structures and capacity calculations.
        /// </remarks>
        /// <param name="id">The unique identifier of the location to delete.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpDelete("api/locations/{id:guid}")]
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

            var result = await _locationService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse location details by its unique identifier.
        /// </summary>
        /// <remarks>
        /// Retrieves properties of a single storage location.
        /// 
        /// Business purpose:
        /// Display information about a location, such as occupied pallets and parent zone name.
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Read-only retrieval for display in lists, maps, or info boxes.
        /// </remarks>
        /// <param name="id">The unique identifier of the location.</param>
        /// <returns>The location details.</returns>
        [HttpGet("api/locations/{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseLocationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _locationService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of locations for a specific zone.
        /// </summary>
        /// <remarks>
        /// Retrieves a page of location records associated with a zone.
        /// 
        /// Business purpose:
        /// Allow warehouse operators to browse locations in a zone (e.g. to inspect capacity).
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Used for dropdown population and inventory location grids.
        /// </remarks>
        /// <param name="zoneId">The unique identifier of the parent zone.</param>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="search">Optional query string to search locations by code or description.</param>
        /// <returns>A paginated list of locations.</returns>
        [HttpGet("api/zones/{zoneId:guid}/locations")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<WarehouseLocationResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
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
