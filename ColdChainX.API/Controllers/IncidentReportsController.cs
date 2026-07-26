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
    /// <summary>
    /// Manages transport and warehouse operational incident reports (e.g. breakdown, spillage, cargo damage).
    /// </summary>
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

        /// <summary>
        /// Log/Report a new operational or transport incident.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Driver,DRIVER,WarehouseOperator")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ReportIncident([FromBody] CreateIncidentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ReportIncidentAsync(request, userId);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// Mobile-friendly incident report endpoint with optional photos/receipts.
        /// </summary>
        [HttpPost("with-evidence")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Driver,DRIVER,WarehouseOperator")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReportIncidentWithEvidence(
            [FromForm] CreateIncidentWithEvidenceRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(ApiResponse<object>.Failure("User ID claim is missing or invalid in the token."));

            var result = await _incidentService.ReportIncidentAsync(
                request,
                userId,
                request.EvidenceFiles);
            if (!result.Success)
                return StatusCode(result.StatusCode, result);

            return Ok(result);
        }

        /// <summary>Add optional incident photos or driver receipts after reporting.</summary>
        [HttpPost("{id:guid}/evidences")]
        [Consumes("multipart/form-data")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Driver,DRIVER")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
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

        /// <summary>
        /// Resolve a reported incident (mark as RESOLVED).
        /// </summary>
        [HttpPost("{id:guid}/resolve")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
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

        /// <summary>
        /// The assigned driver confirms an incident that does not require rescue
        /// was handled on site and continues the original trip/vehicle.
        /// </summary>
        [HttpPost("{id:guid}/continue-trip")]
        [Authorize(Roles = "Driver,DRIVER")]
        [ProducesResponseType(typeof(ApiResponse<IncidentWorkflowResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
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

        /// <summary>
        /// [Luồng 8 - Bước 2, Lookup] Danh sách xe lạnh ACTIVE đủ điều kiện thay thế cho chuyến gặp sự cố.
        /// </summary>
        /// <remarks>
        /// Chỉ trả về các xe: đang ACTIVE, giữ được nhiệt độ mục tiêu của chuyến,
        /// và đủ tải trọng/thể tích để sang toàn bộ LPN đang trên xe hỏng.
        /// Dùng để Điều phối viên chọn ReplacementVehicleId cho POST /dispatch-rescue.
        /// </remarks>
        [HttpGet("{id:guid}/rescue-candidates")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        [ProducesResponseType(typeof(ApiResponse<List<RescueCandidateResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetRescueCandidates([FromRoute] Guid id)
        {
            var result = await _rescueService.GetRescueCandidatesAsync(id);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        /// <summary>
        /// [Luồng 8 - Bước 2+3] Điều xe lạnh thay thế đến hiện trường (Sang xe) và cập nhật lộ trình.
        /// </summary>
        /// <remarks>
        /// Thực hiện trọn vẹn Bước 2 và Bước 3 của luồng xử lý sự cố:
        ///
        ///   - Xe hỏng → MAINTENANCE + tự động mở phiếu sửa chữa (MaintenanceTicket OPEN)
        ///   - Xe thay thế gán vào chuyến → OnTrip; đội bốc xếp nhận lệnh sang toàn bộ LPN (SignalR)
        ///   - Trip.Status → DELAYED
        ///   - Hệ thống tự tính lại ETA các trạm phía trước (Goong, fallback Haversine/shift)
        ///   - Gửi thông báo xin lỗi kèm ETA mới cho tất cả khách hàng đang chờ ở các trạm phía trước
        ///   - Incident.Status → RESCUE_DISPATCHED (đóng hẳn bằng POST /{id}/resolve sau khi hoàn tất)
        /// </remarks>
        [HttpPost("{id:guid}/dispatch-rescue")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER")]
        [ProducesResponseType(typeof(ApiResponse<IncidentRescueResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
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

        /// <summary>
        /// Confirms all cargo has been transferred to the replacement vehicle.
        /// Every IoT device on that vehicle must be online; MQTT streaming must be
        /// published successfully before the trip returns to IN_TRANSIT.
        /// </summary>
        [HttpPost("{id:guid}/confirm-transload")]
        [Authorize(Roles = "Admin,ADMIN,Manager,MANAGER,Dispatcher,DISPATCHER,WarehouseOperator,Loader,LOADER")]
        [ProducesResponseType(typeof(ApiResponse<IncidentWorkflowResult>), StatusCodes.Status200OK)]
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

        /// <summary>Admin approves the amount paid in advance by the driver.</summary>
        [HttpPost("{id:guid}/expenses/approve")]
        [Authorize(Roles = "Admin,ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
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
        [Authorize(Roles = "Admin,ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
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

        /// <summary>
        /// Get details of a specific incident.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ApiResponse<IncidentResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var result = await _incidentService.GetIncidentByIdAsync(id);
            if (!result.Success)
                return NotFound(result);

            return Ok(result);
        }

        /// <summary>
        /// Get a paginated list of operational incidents.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<IncidentResponse>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetList(
            [FromQuery] Guid? tripId = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10)
        {
            var result = await _incidentService.GetPagedIncidentsAsync(tripId, pageNumber, pageSize);
            return Ok(result);
        }
    }
}
