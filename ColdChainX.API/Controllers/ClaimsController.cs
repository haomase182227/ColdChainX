using System;
using System.Collections.Generic;
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

        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseWorker,Customer")]
        public async Task<IActionResult> CreateClaim([FromForm] CreateClaimRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            Guid? customerId = null;
            var isCustomer = User.IsInRole("Customer");
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (Guid.TryParse(customerIdClaim, out var parsedCustomerId))
            {
                customerId = parsedCustomerId;
            }
            else if (isCustomer)
            {
                return Unauthorized(ApiResponse<object>.Failure("CustomerId claim is missing or invalid in the token."));
            }

            var result = await _claimService.CreateClaimAsync(request, userId, customerId, isCustomer);
            if (!result.Success)
                return StatusCode(result.StatusCode != 0 ? result.StatusCode : StatusCodes.Status400BadRequest, result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/resolve")]
        [Authorize(Roles = "Admin,WarehouseWorker,Dispatcher")]
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

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _claimService.GetClaimByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] Guid? orderId = null,
            [FromQuery] string? status = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber <= 0 || pageSize <= 0)
            {
                return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ALL",
                    "OPEN",
                    "PENDING_REVIEW",
                    "PENDING_DISPATCHER_REVIEW",
                    "PENDING_ACCOUNTANT_REVIEW",
                    "RESOLVED",
                    "RESOLVED_PAID",
                    "REJECTED",
                    "CLOSED",
                    "APPROVED"
                };

                if (!validStatuses.Contains(status.Trim()))
                {
                    return BadRequest(ApiResponse<object>.Failure("Invalid status code or pagination parameters."));
                }
            }

            var result = await _claimService.GetPagedClaimsAsync(orderId, status, pageNumber, pageSize);
            return Ok(result);
        }


        [HttpGet("{id:guid}/osd-investigation")]
        [Authorize(Roles = "Admin,Dispatcher,Accountant,WarehouseWorker")]
        public async Task<IActionResult> GetClaimOsdInvestigation([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new ColdChainX.Application.Features.Claims.Queries.GetClaimOsdInvestigationQuery { ClaimId = id });
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/payout")]
        [Authorize(Roles = "Admin,Accountant,Dispatcher")]
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

        [HttpPost("{id:guid}/dispatcher-approve")]
        [HttpPost("{id:guid}/approve-qa")]
        [Authorize(Roles = "Admin,Dispatcher,WarehouseWorker")]
        public async Task<IActionResult> ApproveByQa([FromRoute] Guid id, [FromBody] ApproveClaimByQaCommand command)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("Invalid User ID claim."));

            command.ClaimId = id;
            command.QaUserId = userId;
            var result = await _mediator.Send(command);
            if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
            return Ok(result);
        }

        [HttpPost("{id:guid}/dispatcher-reject")]
        [Authorize(Roles = "Admin,Dispatcher,WarehouseWorker")]
        public async Task<IActionResult> RejectByQa([FromRoute] Guid id, [FromBody] RejectClaimByQaCommand command)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("Invalid User ID claim."));

            command.ClaimId = id;
            command.QaUserId = userId;
            var result = await _mediator.Send(command);
            if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
            return Ok(result);
        }

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
            if (!result.Success) return StatusCode(result.StatusCode != 0 ? result.StatusCode : 400, result);
            return Ok(result);
        }
    }
}
