using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Relocate stock from one location to another (e.g. Put-away or shelf relocation).
        /// </summary>
        [HttpPost("relocate")]
        public async Task<IActionResult> RelocateStock([FromBody] StockRelocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _inventoryService.RelocateStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Adjust stock levels for cycle counts, damages, losses, expirations, quality holds, or findings.
        /// </summary>
        [HttpPost("adjust")]
        public async Task<IActionResult> AdjustStock([FromBody] InventoryAdjustmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _inventoryService.AdjustStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get available stock sorted by FEFO (Expiry Date ascending) with FIFO tie-breaker.
        /// </summary>
        [HttpGet("available")]
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
        [HttpPost("allocate")]
        public async Task<IActionResult> AllocateStock([FromBody] AllocateInventoryRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _inventoryService.AllocateStockAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Release active allocations for a specific reference document.
        /// </summary>
        [HttpPost("release-allocation")]
        public async Task<IActionResult> ReleaseAllocation([FromBody] ReleaseAllocationRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized("User ID claim is missing or invalid in the token");

            var result = await _inventoryService.ReleaseAllocationAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }
    }
}
