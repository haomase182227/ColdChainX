using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using ColdChainX.Application.Features.Claims.Commands;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Claim;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    /// <summary>
    /// Manages customer claims for compensation regarding damaged, lost, or delayed cold chain cargo.
    /// </summary>
    [ApiController]
    [Route("api/v1/claims")]
    [Authorize]
    public class ClaimsController : ControllerBase
    {
        private readonly IClaimService _claimService;
        private readonly IMediator _mediator;

        public ClaimsController(IClaimService claimService, IMediator mediator)
        {
            _claimService = claimService;
            _mediator = mediator;
        }

        /// <summary>
        /// Lodge/Create a new customer claim with evidence attachments.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,WarehouseOperator,WAREHOUSEOPERATOR,Customer,CUSTOMER")]
        [ProducesResponseType(typeof(ApiResponse<ClaimResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateClaim([FromForm] CreateClaimRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _claimService.CreateClaimAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Resolve/Finalize a customer claim (mark as RESOLVED/REJECTED and set fault owner).
        /// </summary>
        [HttpPost("{id:guid}/resolve")]
        [Authorize(Roles = "Admin,ADMIN,WarehouseOperator,WAREHOUSEOPERATOR,WarehouseOperator,WAREHOUSEOPERATOR, Dispatcher")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ResolveClaim([FromRoute] Guid id, [FromBody] ResolveClaimRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _claimService.ResolveClaimAsync(id, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Get details of a specific claim including evidence attachments.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<ClaimResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _claimService.GetClaimByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of claims.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<ClaimResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetList(
            [FromQuery] Guid? orderId = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _claimService.GetPagedClaimsAsync(orderId, pageNumber, pageSize);
            return Ok(result);
        }

        /// <summary>
        /// [Bước 2 - Dispatcher & Sale] Lấy danh sách hồ sơ báo lỗi OS&D Dock chờ Dispatcher mở biểu đồ nhiệt độ IoT Log rà soát.
        /// </summary>
        [HttpGet("pending-dispatcher")]
        [HttpGet("osd-incidents")]
        [HttpGet("/api/Delivery/osd-incidents")]
        [Authorize(Roles = "Admin,Dispatcher,Accountant,WarehouseOperator")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPendingOsdIncidents([FromQuery] string? status = null)
        {
            var result = await _mediator.Send(new ColdChainX.Application.Features.Claims.Queries.GetPendingOsdClaimsQuery { Status = status });
            return Ok(result);
        }

        /// <summary>
        /// Complete payout for a resolved claim.
        /// </summary>
        [HttpPost("{id:guid}/payout")]
        [Authorize(Roles = "Admin,Accountant,Dispatcher")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CompletePayout([FromRoute] Guid id, [FromBody] CompleteClaimPayoutRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _claimService.CompleteClaimPayoutAsync(id, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// [Bước 2 - Dispatcher & Sale] Dispatcher check biểu đồ IoT Log -> Bấm [Duyệt lỗi] chuyển thẳng sang Dashboard Kế toán.
        /// </summary>
        [HttpPost("{id:guid}/dispatcher-approve")]
        [HttpPost("{id:guid}/approve-qa")]
        [Authorize(Roles = "Admin,Dispatcher,WarehouseOperator")]
        public async Task<IActionResult> ApproveByQa([FromRoute] Guid id, [FromBody] ApproveClaimByQaCommand command)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("Invalid User ID claim."));

            command.ClaimId = id;
            command.QaUserId = userId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// [Bước 2 - Dispatcher & Sale] Dispatcher check biểu đồ IoT Log -> Bấm [Từ chối bồi thường] khép lại hồ sơ.
        /// </summary>
        [HttpPost("{id:guid}/dispatcher-reject")]
        [Authorize(Roles = "Admin,Dispatcher,WarehouseOperator")]
        public async Task<IActionResult> RejectByQa([FromRoute] Guid id, [FromBody] RejectClaimByQaCommand command)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("Invalid User ID claim."));

            command.ClaimId = id;
            command.QaUserId = userId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// [Fast-Track 24h] Kế toán chi tiền đền bù (Cash Refund) trong vòng 24h và đóng luồng khiếu nại.
        /// </summary>
        [HttpPost("{id:guid}/payout-accountant")]
        [Authorize(Roles = "Admin,Accountant")]
        public async Task<IActionResult> PayoutByAccountant([FromRoute] Guid id, [FromBody] PayoutClaimByAccountantCommand command)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("Invalid User ID claim."));

            command.ClaimId = id;
            command.AccountantUserId = userId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }
    }
}
