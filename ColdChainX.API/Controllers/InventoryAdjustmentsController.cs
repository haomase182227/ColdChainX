using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Enums;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/inventory-adjustments")]
    [Authorize]
    public class InventoryAdjustmentsController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryAdjustmentsController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateAdjustmentRequest([FromBody] InventoryAdjustmentRequest request)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _inventoryService.CreateAdjustmentRequestAsync(request, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetAdjustmentById), new { id = result.Data }, result);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> GetAdjustments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] InventoryAdjustmentStatus? status = null)
        {
            var result = await _inventoryService.GetPagedAdjustmentsAsync(pageNumber, pageSize, status);
            return Ok(result);
        }

        [HttpGet("pending")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> GetPendingAdjustments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _inventoryService.GetPagedAdjustmentsAsync(pageNumber, pageSize, InventoryAdjustmentStatus.PENDING_APPROVAL);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetAdjustmentById(Guid id)
        {
            var result = await _inventoryService.GetAdjustmentByIdAsync(id);
            if (!result.Success) return NotFound(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/approve")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> ApproveAdjustment(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _inventoryService.ApproveAdjustmentAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/reject")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> RejectAdjustment(Guid id, [FromBody] RejectAdjustmentRequest request)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

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
