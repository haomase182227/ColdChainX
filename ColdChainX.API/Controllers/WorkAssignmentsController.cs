using System.Security.Claims;
using ColdChainX.API.Authorization;
using ColdChainX.Application.DTOs.WorkAssignments;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/work-assignments")]
public sealed class WorkAssignmentsController : ControllerBase
{
    private readonly IWorkAssignmentService _workAssignmentService;

    public WorkAssignmentsController(IWorkAssignmentService workAssignmentService)
    {
        _workAssignmentService = workAssignmentService;
    }

    [HttpPost]
    [HasPermission(PermissionCodes.WorkAssignmentManage)]
    public async Task<ActionResult<ApiResponse<WorkAssignmentDto>>> Create(
        [FromBody] CreateWorkAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.CreateAsync(
            request,
            GetCurrentUserId(),
            cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<WorkAssignmentDto>.SuccessResponse(
                result,
                "Work assignment created successfully",
                StatusCodes.Status201Created));
    }

    [HttpGet]
    [HasPermission(PermissionCodes.WorkAssignmentManage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<WorkAssignmentDto>>>> GetAll(
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.GetAllAsync(
            assignedToUserId,
            warehouseId,
            status,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyCollection<WorkAssignmentDto>>.SuccessResponse(
            result,
            "Work assignments retrieved successfully"));
    }

    [HttpGet("me")]
    [HasPermission(PermissionCodes.WorkAssignmentViewOwn)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<WorkAssignmentDto>>>> GetMine(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.GetMineAsync(
            GetCurrentUserId(),
            status,
            cancellationToken);

        return Ok(ApiResponse<IReadOnlyCollection<WorkAssignmentDto>>.SuccessResponse(
            result,
            "My work assignments retrieved successfully"));
    }

    [HttpPut("{assignmentId:guid}/start")]
    [HasPermission(PermissionCodes.WorkAssignmentExecute)]
    public async Task<ActionResult<ApiResponse<WorkAssignmentDto>>> Start(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.StartAsync(
            assignmentId,
            GetCurrentUserId(),
            cancellationToken);

        return Ok(ApiResponse<WorkAssignmentDto>.SuccessResponse(
            result,
            "Work assignment started successfully"));
    }

    [HttpPut("{assignmentId:guid}/complete")]
    [HasPermission(PermissionCodes.WorkAssignmentExecute)]
    public async Task<ActionResult<ApiResponse<WorkAssignmentDto>>> Complete(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.CompleteAsync(
            assignmentId,
            GetCurrentUserId(),
            cancellationToken);

        return Ok(ApiResponse<WorkAssignmentDto>.SuccessResponse(
            result,
            "Work assignment completed successfully"));
    }

    [HttpPut("{assignmentId:guid}/cancel")]
    [HasPermission(PermissionCodes.WorkAssignmentManage)]
    public async Task<ActionResult<ApiResponse<WorkAssignmentDto>>> Cancel(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        var result = await _workAssignmentService.CancelAsync(assignmentId, cancellationToken);
        return Ok(ApiResponse<WorkAssignmentDto>.SuccessResponse(
            result,
            "Work assignment cancelled successfully"));
    }

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}
