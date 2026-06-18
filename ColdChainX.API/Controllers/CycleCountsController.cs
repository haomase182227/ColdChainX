using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.DTOs.CycleCount;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Enums;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages stock cycle counting audits and discrepancies reconciliation.
    /// </summary>
    [ApiController]
    [Route("api/v1/cycle-counts")]
    [Authorize]
    public class CycleCountsController : ControllerBase
    {
        private readonly ICycleCountService _cycleCountService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CycleCountsController"/> class.
        /// </summary>
        /// <param name="cycleCountService">The service used to manage cycle counts.</param>
        public CycleCountsController(ICycleCountService cycleCountService)
        {
            _cycleCountService = cycleCountService;
        }

        /// <summary>
        /// Create a new cycle count audit plan.
        /// </summary>
        /// <remarks>
        /// Registers a plan targeting specific locations/zones to be audited.
        /// 
        /// Business purpose:
        /// Maintain inventory accuracy by scheduling regular physical counts of stored products.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// Creates a plan in DRAFT status, listing target locations and their expected system quantities.
        /// </remarks>
        /// <param name="dto">The plan creation parameters containing assigned operator, warehouse, and target scope.</param>
        /// <returns>The created cycle count plan details.</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<CycleCountPlanResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CreatePlan([FromBody] CreateCycleCountPlanDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _cycleCountService.CreatePlanAsync(dto, userId);
            if (!result.Success) return BadRequest(result);

            return CreatedAtAction(nameof(GetPlanById), new { id = result.Data.PlanId }, result);
        }

        /// <summary>
        /// Start executing the cycle count audit plan.
        /// </summary>
        /// <remarks>
        /// Changes plan status to IN_PROGRESS, signaling to operators that counting should begin.
        /// 
        /// Business purpose:
        /// Lock audit scope and freeze baseline system inventory counts.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Activates count entries.baseline system baseline quantities are captured.
        /// </remarks>
        /// <param name="id">The unique identifier of the cycle count plan.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id}/start")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,WarehouseOperator")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> StartCounting(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _cycleCountService.StartCountingAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Submit physical counts for a cycle count plan.
        /// </summary>
        /// <remarks>
        /// Submits verified physical quantities and pallet counts for audit entries.
        /// 
        /// Business purpose:
        /// Record real physical stock values from warehouse counts.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Calculates variance quantities. Entries with no variance are reconciled automatically. Discrepancies are queued for review.
        /// </remarks>
        /// <param name="id">The unique identifier of the cycle count plan.</param>
        /// <param name="dto">The list of counted quantities per entry.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("{id}/submit")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,WarehouseOperator")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SubmitCounts(Guid id, [FromBody] SubmitCycleCountsDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _cycleCountService.SubmitCountsAsync(id, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Review and resolve a cycle count entry discrepancy.
        /// </summary>
        /// <remarks>
        /// Approves or rejects the variance discovered during physical counts.
        /// 
        /// Business purpose:
        /// Reconcile system records with actual stock when discrepancies are found.
        /// 
        /// Required roles:
        /// Admin, Manager
        /// 
        /// Workflow impact:
        /// If approved, generates an automated stock adjustment to correct on-hand quantities.
        /// </remarks>
        /// <param name="entryId">The unique identifier of the count entry to review.</param>
        /// <param name="dto">The approval decision and notes.</param>
        /// <returns>A boolean success indicator wrapped in ApiResponse.</returns>
        [HttpPost("entries/{entryId}/review")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ReviewVariance(Guid entryId, [FromBody] ReviewVarianceDto dto)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _cycleCountService.ReviewVarianceAsync(entryId, dto, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get cycle count plan details by ID.
        /// </summary>
        /// <remarks>
        /// Retrieves properties and entry lists of a single count plan by its GUID.
        /// 
        /// Business purpose:
        /// View count entries, system quantities, and verified audit results.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only retrieval.
        /// </remarks>
        /// <param name="id">The unique identifier of the cycle count plan.</param>
        /// Outbound
        /// <returns>The cycle count plan details.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<CycleCountPlanResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPlanById(Guid id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid."));

            var result = await _cycleCountService.GetPlanDetailsAsync(id, userId);
            if (!result.Success) return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of cycle count plans.
        /// </summary>
        /// <remarks>
        /// Retrieves a page of cycle count audit plans.
        /// 
        /// Business purpose:
        /// Monitor active audits and browse audit history.
        /// 
        /// Required roles:
        /// Authenticated user.
        /// 
        /// Workflow impact:
        /// Read-only historical lookup.
        /// </remarks>
        /// <param name="pageNumber">The index of the page, starting at 1.</param>
        /// <param name="pageSize">The number of records per page.</param>
        /// <param name="status">Optional status query filter.</param>
        /// <param name="warehouseId">Optional warehouse query filter.</param>
        /// <returns>A paginated list of cycle count plans.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<CycleCountPlanResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
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
