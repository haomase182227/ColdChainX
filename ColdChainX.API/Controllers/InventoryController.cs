using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages warehouse inventory operations including relocation, adjustments, availability queries, and allocations.
    /// </summary>
    [ApiController]
    [Route("api/v1/inventory")]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        /// <summary>
        /// Initializes a new instance of the <see cref="InventoryController"/> class.
        /// </summary>
        /// <param name="inventoryService">The service used to manage inventory.</param>
        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Relocate stock from one location to another (e.g. Put-away or shelf relocation).
        /// </summary>
        /// <remarks>
        /// Records the physical movement of stock items between warehouse storage coordinates.
        /// 
        /// Business purpose:
        /// Track stock movements when executing put-away tasks, replenishment, or slotting optimization.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Deducts pallet counts from the source location, adds them to the destination, and records a stock movement log.
        /// </remarks>
        /// <param name="request">The relocation parameters including source/destination coordinates, batch, and quantity.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("relocations")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RelocateStock([FromBody] StockRelocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token"));

            var result = await _inventoryService.RelocateStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Adjust stock levels for cycle counts, damages, losses, expirations, quality holds, or findings.
        /// </summary>
        /// <remarks>
        /// Directly alters inventory counts, bypassing standard receipt/shipping workflows. Used for write-offs or correction deltas.
        /// 
        /// Business purpose:
        /// Update inventory counts in response to physical audits, spoilage, damages, or discrepancies.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Creates an adjustment record that is executed immediately or submitted for manager approval.
        /// </remarks>
        /// <param name="request">The adjustment parameters detailing deltas or absolute quantities.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("adjustments")]
        [Authorize(Roles = "Admin,ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> AdjustStock([FromBody] InventoryAdjustmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token"));

            var result = await _inventoryService.AdjustStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get available stock sorted by FEFO (Expiry Date ascending) with FIFO tie-breaker.
        /// </summary>
        /// <remarks>
        /// Retrieves a paginated list of unallocated inventory items, sorted to prioritize older products.
        /// 
        /// Business purpose:
        /// Browse available stock to determine shipping feasibility and ensure compliance with FEFO shipping rules.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only browse operation representing the current state of active physical stock.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="itemCode">Optional product code query filter.</param>
        /// <returns>A paginated list of available stock items.</returns>
        [HttpGet("available")]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<AvailableStockResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAvailableStock(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? itemCode = null)
        {
            var result = await _inventoryService.GetAvailableStockAsync(pageNumber, pageSize, itemCode);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Allocate inventory atomically for a specific outbound reference document (e.g., transport order).
        /// </summary>
        /// <remarks>
        /// Dynamically assigns physical stock batches and locations to an outbound shipping document using FEFO allocation rules.
        /// 
        /// Business purpose:
        /// Reserve stock items for a customer shipment, preventing double-allocation or picking of expired goods.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Locks the quantity of stock as "allocated", preventing it from being picked by other tasks.
        /// </remarks>
        /// <param name="request">The allocation request specifying the outbound document and items requested.</param>
        /// <returns>The allocation details including selected locations and batch numbers.</returns>
        [HttpPost("allocations")]
        [ProducesResponseType(typeof(ApiResponse<AllocationResultResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> AllocateStock([FromBody] AllocateInventoryRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token"));

            var result = await _inventoryService.AllocateStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Release active allocations for a specific reference document.
        /// </summary>
        /// <remarks>
        /// Unlocks previously reserved stock allocations back to general available inventory.
        /// 
        /// Business purpose:
        /// Free up reserved stock when a customer order is cancelled or modified.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Decrements allocated quantities and restores available quantities.
        /// </remarks>
        /// <param name="request">The release request specifying the document identifier.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpDelete("allocations")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ReleaseAllocation([FromBody] ReleaseAllocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token"));

            var result = await _inventoryService.ReleaseAllocationAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
