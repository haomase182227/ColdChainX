using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages warehouse entities, including creation, updates, listing, and deletion.
    /// </summary>
    [ApiController]
    [Route("api/v1/warehouses")]
    [Authorize]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;

        /// <summary>
        /// Initializes a new instance of the <see cref="WarehouseController"/> class.
        /// </summary>
        /// <param name="warehouseService">The service used to manage warehouses.</param>
        public WarehouseController(IWarehouseService warehouseService)
        {
            _warehouseService = warehouseService;
        }

        /// <summary>
        /// Create a new warehouse.
        /// </summary>
        /// <remarks>
        /// Registers a new warehouse facility in the system.
        /// 
        /// Business purpose:
        /// Setup physical storage facilities to receive, store, and dispatch cold-chain products.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Creates the parent entity under which storage zones and specific storage locations can be configured.
        /// </remarks>
        /// <param name="request">The warehouse creation request containing code, name, capacity, and temperature limits.</param>
        /// <returns>The newly created warehouse details.</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _warehouseService.CreateAsync(request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Update an existing warehouse.
        /// </summary>
        /// <remarks>
        /// Updates the attributes of an existing warehouse.
        /// 
        /// Business purpose:
        /// Modify name, type, capacity, address, or temperature parameters as operations evolve.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Instantly changes default compliance thresholds for inbound quality checks and capacity warnings.
        /// </remarks>
        /// <param name="id">The unique identifier of the warehouse to update.</param>
        /// <param name="request">The updated warehouse details.</param>
        /// <returns>The updated warehouse details.</returns>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Admin,Manager")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _warehouseService.UpdateAsync(id, request, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Soft-delete a warehouse.
        /// </summary>
        /// <remarks>
        /// Deactivates and marks a warehouse as deleted.
        /// 
        /// Business purpose:
        /// Decommission a warehouse when it is no longer in service.
        /// 
        /// Required roles:
        /// Admin
        /// 
        /// Workflow impact:
        /// Blocks any future inbound receipts or cycle counts at this warehouse.
        /// </remarks>
        /// <param name="id">The unique identifier of the warehouse to delete.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpDelete("{id:guid}")]
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

            var result = await _warehouseService.DeleteAsync(id, currentUserId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get warehouse details by its unique identifier.
        /// </summary>
        /// <remarks>
        /// Retrieves a warehouse by its GUID.
        /// 
        /// Business purpose:
        /// Display warehouse parameters, location coordinates, or capacity.
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Information lookup for UI display or child-entity references.
        /// </remarks>
        /// <param name="id">The unique identifier of the warehouse.</param>
        /// <returns>The warehouse details.</returns>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<WarehouseResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _warehouseService.GetByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of warehouses with optional keyword search.
        /// </summary>
        /// <remarks>
        /// Retrieves a page of warehouse records.
        /// 
        /// Business purpose:
        /// Display active and configured warehouses in lists or dropdowns.
        /// 
        /// Required roles:
        /// All authenticated users.
        /// 
        /// Workflow impact:
        /// Provides a search interface for operators selecting warehouses.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="search">Optional query string to search by code or name.</param>
        /// <returns>A paginated list of warehouses.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<WarehouseResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetList(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var result = await _warehouseService.GetListAsync(pageNumber, pageSize, search);
            return Ok(result);
        }

        /// <summary>
        /// Retrieves all IN_STOCK LPNs in a specific warehouse.
        /// </summary>
        /// <param name="warehouseId">The unique identifier of the warehouse.</param>
        /// <param name="page">Page number (default 1).</param>
        /// <param name="pageSize">Page size (default 10).</param>
        /// <returns>A paginated list of LPNs.</returns>
        [HttpGet("{warehouseId}/lpns")]
        public async Task<ActionResult<ApiResponse<PagedResult<LpnResponse>>>> GetLpnsInWarehouse(
            [FromRoute] Guid warehouseId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var response = await _warehouseService.GetLpnsInWarehouseAsync(warehouseId, page, pageSize);
            return Ok(response);
        }
    }
}
