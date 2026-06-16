using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages stock holding locks (quarantine, quality logs, temperature deviations).
    /// </summary>
    [ApiController]
    [Route("api/v1/inventory-holds")]
    [Authorize]
    public class InventoryHoldsController : ControllerBase
    {
        private readonly IInventoryHoldService _holdService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InventoryHoldsController"/> class.
        /// </summary>
        /// <param name="holdService">The service used to manage inventory holds.</param>
        public InventoryHoldsController(IInventoryHoldService holdService)
        {
            _holdService = holdService;
        }

        /// <summary>
        /// Place stock on hold (e.g. Quarantine or QA inspection).
        /// </summary>
        /// <remarks>
        /// Locks physical stock from allocation or movement.
        /// 
        /// Business purpose:
        /// Prevent damaged, expired, or temperature-deviated products from being dispatched.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Creates an active hold record, reduces available stock quantity, and optionally relocates cargo to quarantine areas.
        /// </remarks>
        /// <param name="dto">The hold parameters detailing quantity, reason, and target quarantine location.</param>
        /// <returns>The created hold record details.</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<HoldResponseDto>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CreateHold([FromBody] CreateInventoryHoldDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _holdService.CreateHoldAsync(dto, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetHoldById), new { id = result.Data.HoldId }, result);
        }

        /// <summary>
        /// Get a paginated list of inventory holds.
        /// </summary>
        /// <remarks>
        /// Retrieves hold records filtered by status, reason, or item code.
        /// 
        /// Business purpose:
        /// Monitor active quarantines and review historical QA locks.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only historical lookup.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="status">Optional status query filter (e.g., ACTIVE, RELEASED).</param>
        /// <param name="reasonCode">Optional reason code query filter.</param>
        /// <param name="itemCode">Optional product code query filter.</param>
        /// <returns>A paginated list of holds.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<HoldResponseDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetHolds(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null,
            [FromQuery] string? reasonCode = null,
            [FromQuery] string? itemCode = null)
        {
            var result = await _holdService.GetPagedHoldsAsync(pageNumber, pageSize, status, reasonCode, itemCode);
            return Ok(result);
        }

        /// <summary>
        /// Get details of an inventory hold by ID.
        /// </summary>
        /// <remarks>
        /// Retrieves details of a single hold record by its GUID.
        /// 
        /// Business purpose:
        /// Inspect audit logs and supervisor release notes for a specific quarantine hold.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only details retrieval.
        /// </remarks>
        /// <param name="id">The unique identifier of the hold record.</param>
        /// <returns>The hold record details.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<HoldResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetHoldById(Guid id)
        {
            var result = await _holdService.GetHoldByIdAsync(id);
            if (!result.Success) return NotFound(result);
            return Ok(result);
        }

        /// <summary>
        /// Release an active inventory hold.
        /// </summary>
        /// <remarks>
        /// Unlocks the held stock, making it available again for outbound allocation or relocation.
        /// 
        /// Business purpose:
        /// Authorize stock release after QA testing has passed.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Restores stock availability status and optionally moves products back to pick locations.
        /// </remarks>
        /// <param name="id">The unique identifier of the hold record.</param>
        /// <param name="dto">The release parameters including resolution notes and optional destination location.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id}/release")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ReleaseHold(Guid id, [FromBody] ReleaseInventoryHoldDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _holdService.ReleaseHoldAsync(id, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Adjust out (write-off) held inventory.
        /// </summary>
        /// <remarks>
        /// Disposes and writes off quarantined stock, permanently removing it from inventory.
        /// 
        /// Business purpose:
        /// Decommission spoiled, damaged, or failed product batches.
        /// 
        /// Required roles:
        /// Admin
        /// 
        /// Workflow impact:
        /// Reduces physical QuantityOnHand and PalletCount at the location, closing the hold record.
        /// </remarks>
        /// <param name="id">The unique identifier of the hold record.</param>
        /// <param name="reasonNotes">Rejection/disposal description note.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id}/adjust-out")]
        [Authorize(Roles = "Admin,ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> AdjustOutHold(Guid id, [FromBody] string reasonNotes)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _holdService.AdjustOutHoldAsync(id, reasonNotes, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }
    }
}
