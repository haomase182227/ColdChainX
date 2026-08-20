using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.API.Controllers
{
    [ApiController]
    [Route("api/v1/incidents")]
    [Authorize]
    public class IncidentReportsController : ControllerBase
    {
        private readonly IIncidentReportService _incidentService;
        private readonly IIncidentRescueService _rescueService;

        public IncidentReportsController(
            IIncidentReportService incidentService,
            IIncidentRescueService rescueService)
        {
            _incidentService = incidentService;
            _rescueService = rescueService;
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Driver,Dispatcher")]
        public async Task<IActionResult> ReportIncident([FromForm] CreateIncidentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ReportIncidentAsync(request, userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/evidences")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Admin,Driver,Dispatcher")]
        public async Task<IActionResult> AddEvidence(
            [FromRoute] Guid id,
            [FromForm] UploadIncidentEvidenceRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.AddEvidenceAsync(
                id,
                request.Files,
                request.EvidenceType,
                userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/resolve")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> ResolveIncident([FromRoute] Guid id, [FromBody] ResolveIncidentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ResolveIncidentAsync(id, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/continue-trip")]
        [Authorize(Roles = "Driver,Dispatcher")]
        public async Task<IActionResult> ContinueTrip(
            [FromRoute] Guid id,
            [FromBody] ContinueTripAfterIncidentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.ContinueTripAsync(id, request, userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        [HttpGet("{id:guid}/rescue-candidates")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> GetRescueCandidates([FromRoute] Guid id)
        {
            var result = await _rescueService.GetRescueCandidatesAsync(id);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/assess-risk")]
        [Authorize(Roles = "Admin,Driver,Dispatcher")]
        public async Task<IActionResult> AssessRisk(
            [FromRoute] Guid id,
            [FromBody] AssessIncidentRiskRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.AssessRiskAsync(id, request, userId);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
        }

        [HttpGet("{id:guid}/rescue-options")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> GetRescueOptions([FromRoute] Guid id)
        {
            var result = await _rescueService.GetRescuePlanAsync(id);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:guid}/record-fallback")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> RecordFallback(
            [FromRoute] Guid id,
            [FromBody] RecordRescueFallbackRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.RecordFallbackAsync(id, request, userId);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:guid}/external-reefer-dispatch")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> DispatchExternalReefer(
            [FromRoute] Guid id,
            [FromBody] DispatchExternalReeferRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.DispatchExternalReeferAsync(id, request, userId);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:guid}/inbound-route-warehouse")]
        [Authorize(Roles = "Admin,Dispatcher,WarehouseWorker")]
        public async Task<IActionResult> InboundRouteWarehouse(
            [FromRoute] Guid id,
            [FromBody] InboundRouteWarehouseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.InboundRouteWarehouseAsync(id, request, userId);
            return result.Success ? Ok(result) : StatusCode(result.StatusCode, result);
        }

        [HttpPost("{id:guid}/dispatch-rescue")]
        [Authorize(Roles = "Admin,Dispatcher")]
        public async Task<IActionResult> DispatchRescue([FromRoute] Guid id, [FromBody] DispatchRescueRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.DispatchRescueAsync(id, request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/confirm-transload")]
        [Authorize(Roles = "Admin,Dispatcher,Driver")]
        public async Task<IActionResult> ConfirmTransload(
            [FromRoute] Guid id,
            [FromBody] ConfirmTransloadRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _rescueService.ConfirmTransloadAsync(id, request, userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        [HttpPost("{id:guid}/expenses/approve")]
        [Authorize(Roles = "Admin,ADMIN,Accountant,ACCOUNTANT")]
        public async Task<IActionResult> ApproveExpense(
            [FromRoute] Guid id,
            [FromBody] ApproveIncidentExpenseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ApproveExpenseAsync(id, request, userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        /// <summary>
        /// Admin records the reimbursement, uploads its receipt and sends it to
        /// the reporting driver through persistent and realtime notifications.
        /// </summary>
        [HttpPost("{id:guid}/expenses/reimburse")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Admin,ADMIN,Accountant,ACCOUNTANT")]
        public async Task<IActionResult> ReimburseExpense(
            [FromRoute] Guid id,
            [FromForm] ReimburseIncidentExpenseRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ReimburseExpenseAsync(id, request, userId);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _incidentService.GetIncidentByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] Guid? tripId = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            if (pageNumber <= 0 || pageSize <= 0)
                return BadRequest(ApiResponse<object>.Failure("PageNumber and PageSize must be greater than zero."));

            var result = await _incidentService.GetPagedIncidentsAsync(tripId, pageNumber, pageSize);
            return Ok(result);
        }
    }
}
