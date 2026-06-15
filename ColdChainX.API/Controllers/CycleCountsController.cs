using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.CycleCount;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Enums;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/cycle-counts")]
    [Authorize]
    public class CycleCountsController : ControllerBase
    {
        private readonly ICycleCountService _cycleCountService;

        public CycleCountsController(ICycleCountService cycleCountService)
        {
            _cycleCountService = cycleCountService;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> CreatePlan([FromBody] CreateCycleCountPlanDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _cycleCountService.CreatePlanAsync(dto, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetPlanById), new { id = result.Data.PlanId }, result);
        }

        [HttpPost("{id}/start")]
        public async Task<IActionResult> StartCounting(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _cycleCountService.StartCountingAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id}/submit")]
        public async Task<IActionResult> SubmitCounts(Guid id, [FromBody] SubmitCycleCountsDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _cycleCountService.SubmitCountsAsync(id, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("entries/{entryId}/review")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        public async Task<IActionResult> ReviewVariance(Guid entryId, [FromBody] ReviewVarianceDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _cycleCountService.ReviewVarianceAsync(entryId, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPlanById(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var result = await _cycleCountService.GetPlanDetailsAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] CycleCountPlanStatus? status = null,
            [FromQuery] Guid? warehouseId = null)
        {
            var result = await _cycleCountService.GetPagedPlansAsync(pageNumber, pageSize, status, warehouseId);
            return Ok(result);
        }

        private Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }
    }
}
