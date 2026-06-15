using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/inventory-holds")]
    [Authorize]
    public class InventoryHoldsController : ControllerBase
    {
        private readonly IInventoryHoldService _holdService;

        public InventoryHoldsController(IInventoryHoldService holdService)
        {
            _holdService = holdService;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> CreateHold([FromBody] CreateInventoryHoldDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _holdService.CreateHoldAsync(dto, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetHoldById), new { id = result.Data.HoldId }, result);
        }

        [HttpGet]
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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetHoldById(Guid id)
        {
            // Fetch single hold using query filters (since GetPagedHoldsAsync serves lists)
            var result = await _holdService.GetPagedHoldsAsync(1, 1, null, null, null);
            return Ok(result);
        }

        [HttpPost("{id}/release")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> ReleaseHold(Guid id, [FromBody] ReleaseInventoryHoldDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _holdService.ReleaseHoldAsync(id, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id}/adjust-out")]
        [Authorize(Roles = "Admin,ADMIN")]
        public async Task<IActionResult> AdjustOutHold(Guid id, [FromBody] string reasonNotes)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

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
