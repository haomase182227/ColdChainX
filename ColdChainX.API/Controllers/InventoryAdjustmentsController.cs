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
using ColdChainX.Core.Enums;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages stock adjustment requests requiring supervisor or manager reviews before posting.
    /// </summary>
    [ApiController]
    [Route("api/v1/inventory-adjustments")]
    [Authorize]
    public class InventoryAdjustmentsController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InventoryAdjustmentsController"/> class.
        /// </summary>
        /// <param name="inventoryService">The service used to manage inventory adjustments.</param>
        public InventoryAdjustmentsController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Create a new stock adjustment request.
        /// </summary>
        /// <remarks>
        /// Submits an inventory count correction or write-off request.
        /// 
        /// Business purpose:
        /// Report stock discrepancies, damages, or expirations noticed on the warehouse floor.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Creates an adjustment record in PENDING_APPROVAL status. Stock is not updated until approved.
        /// </remarks>
        /// <param name="request">The adjustment details including delta/absolute quantities and reasons.</param>
        /// <returns>The unique identifier of the created adjustment request.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateAdjustmentRequest([FromBody] InventoryAdjustmentRequest request)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _inventoryService.CreateAdjustmentRequestAsync(request, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetAdjustmentById), new { id = result.Data }, result);
        }

        /// <summary>
        /// Get a paginated list of all inventory adjustments.
        /// </summary>
        /// <remarks>
        /// Retrieves a log page of stock adjustment records filtered optionally by status.
        /// 
        /// Business purpose:
        /// Audit past stock changes and review audit history.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Read-only historical data lookup.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="status">Optional status query filter.</param>
        /// <returns>A paginated list of adjustment records.</returns>
        [HttpGet]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<InventoryAdjustmentResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetAdjustments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] InventoryAdjustmentStatus? status = null)
        {
            var result = await _inventoryService.GetPagedAdjustmentsAsync(pageNumber, pageSize, status);
            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of pending inventory adjustments.
        /// </summary>
        /// <remarks>
        /// Retrieves adjustment requests awaiting supervisor/manager approval.
        /// 
        /// Business purpose:
        /// Review queue of pending stock corrections before they are finalized.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Read-only check-list for approval tasks.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <returns>A paginated list of pending adjustments.</returns>
        [HttpGet("pending")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<InventoryAdjustmentResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPendingAdjustments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _inventoryService.GetPagedAdjustmentsAsync(pageNumber, pageSize, InventoryAdjustmentStatus.PENDING_APPROVAL);
            return Ok(result);
        }

        /// <summary>
        /// Get details of an inventory adjustment by ID.
        /// </summary>
        /// <remarks>
        /// Retrieves adjustment properties by its GUID.
        /// 
        /// Business purpose:
        /// Examine details of a single inventory correction request.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only detail view.
        /// </remarks>
        /// <param name="id">The unique identifier of the adjustment request.</param>
        /// <returns>The adjustment request details.</returns>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<InventoryAdjustmentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAdjustmentById(Guid id)
        {
            var result = await _inventoryService.GetAdjustmentByIdAsync(id);
            if (!result.Success) return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Approve a pending inventory adjustment.
        /// </summary>
        /// <remarks>
        /// Approves the request, triggers a physical stock update, and logs a stock movement.
        /// 
        /// Business purpose:
        /// Finalize and post-audit count corrections or write-offs.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Updates the real-time QuantityOnHand and PalletCount in the storage location.
        /// </remarks>
        /// <param name="id">The unique identifier of the adjustment request to approve.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id:guid}/approve")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ApproveAdjustment(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _inventoryService.ApproveAdjustmentAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Reject a pending inventory adjustment.
        /// </summary>
        /// <remarks>
        /// Rejects the adjustment request and records the reason.
        /// 
        /// Business purpose:
        /// Refuse an incorrect write-off or request a recount from warehouse staff.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Sets status to REJECTED. No changes are applied to physical stock.
        /// </remarks>
        /// <param name="id">The unique identifier of the adjustment request to reject.</param>
        /// <param name="request">The rejection explanation reason.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id:guid}/reject")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> RejectAdjustment(Guid id, [FromBody] RejectAdjustmentRequest request)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            if (request == null || string.IsNullOrWhiteSpace(request.RejectionReason))
                return BadRequest(ApiResponse<bool>.Failure("Rejection reason is required."));

            var result = await _inventoryService.RejectAdjustmentAsync(id, request.RejectionReason, userId);
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
