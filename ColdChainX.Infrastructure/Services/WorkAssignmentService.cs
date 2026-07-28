using ColdChainX.Application.DTOs.WorkAssignments;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public sealed class WorkAssignmentService : IWorkAssignmentService
{
    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "LOW", "NORMAL", "HIGH", "URGENT"
    };

    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;

    public WorkAssignmentService(ApplicationDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<WorkAssignmentDto> CreateAsync(
        CreateWorkAssignmentRequest request,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateRequest(request);

        var permissionCode = request.RequiredPermissionCode.Trim().ToUpperInvariant();
        var permissionExists = await _db.Permissions
            .AsNoTracking()
            .AnyAsync(p => p.IsActive && p.PermCode == permissionCode, cancellationToken);
        if (!permissionExists)
            throw new ValidationException("Required permission does not exist or is inactive");

        var assignee = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.AssignedToUserId, cancellationToken)
            ?? throw new NotFoundException("Assigned user not found");

        if (string.Equals(assignee.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Cannot assign work to an inactive user");

        var warehouseId = request.WarehouseId ?? assignee.WarehouseId;
        if (request.WarehouseId.HasValue
            && assignee.WarehouseId.HasValue
            && assignee.WarehouseId != request.WarehouseId)
        {
            throw new ValidationException("Assigned user does not belong to the selected warehouse");
        }

        if (!await _permissionService.HasPermissionAsync(assignee.UserId, permissionCode, cancellationToken))
            throw new ForbiddenException("Assigned user does not have the permission required for this work");

        var referenceType = request.ReferenceType.Trim().ToUpperInvariant();
        var referenceId = request.ReferenceId.Trim();
        var taskType = request.TaskType.Trim().ToUpperInvariant();
        var duplicateExists = await _db.WorkAssignments
            .AsNoTracking()
            .AnyAsync(
                assignment => assignment.TaskType == taskType
                              && assignment.ReferenceType == referenceType
                              && assignment.ReferenceId == referenceId
                              && assignment.AssignedToUserId == assignee.UserId
                              && assignment.Status != WorkAssignmentStatuses.Completed
                              && assignment.Status != WorkAssignmentStatuses.Cancelled,
                cancellationToken);

        if (duplicateExists)
            throw new ConflictException("An active assignment already exists for this user and work item");

        var assignment = new WorkAssignment
        {
            AssignmentId = Guid.NewGuid(),
            TaskType = taskType,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            RequiredPermissionCode = permissionCode,
            WarehouseId = warehouseId,
            AssignedToUserId = assignee.UserId,
            AssignedByUserId = assignedByUserId,
            Priority = request.Priority.Trim().ToUpperInvariant(),
            Status = WorkAssignmentStatuses.Assigned,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            AssignedAt = DbNow(),
            DueAt = ToDbDate(request.DueAt)
        };

        _db.WorkAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(assignment, assignee.FullName);
    }

    public async Task<IReadOnlyCollection<WorkAssignmentDto>> GetMineAsync(
        Guid userId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.WorkAssignments
            .AsNoTracking()
            .Include(a => a.AssignedToUser)
            .Where(a => a.AssignedToUserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = query.Where(a => a.Status == normalizedStatus);
        }

        var items = await query
            .OrderByDescending(a => a.Priority == "URGENT")
            .ThenBy(a => a.DueAt)
            .ThenByDescending(a => a.AssignedAt)
            .ToListAsync(cancellationToken);

        return items.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyCollection<WorkAssignmentDto>> GetAllAsync(
        Guid? assignedToUserId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.WorkAssignments.AsNoTracking().Include(a => a.AssignedToUser).AsQueryable();

        if (assignedToUserId.HasValue)
            query = query.Where(a => a.AssignedToUserId == assignedToUserId.Value);
        if (warehouseId.HasValue)
            query = query.Where(a => a.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = query.Where(a => a.Status == normalizedStatus);
        }

        var items = await query
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync(cancellationToken);

        return items.Select(ToDto).ToArray();
    }

    public Task<WorkAssignmentDto> StartAsync(
        Guid assignmentId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => ChangeAssignedUserStatusAsync(
            assignmentId,
            userId,
            WorkAssignmentStatuses.Assigned,
            WorkAssignmentStatuses.InProgress,
            cancellationToken);

    public Task<WorkAssignmentDto> CompleteAsync(
        Guid assignmentId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => ChangeAssignedUserStatusAsync(
            assignmentId,
            userId,
            WorkAssignmentStatuses.InProgress,
            WorkAssignmentStatuses.Completed,
            cancellationToken);

    public async Task<WorkAssignmentDto> CancelAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _db.WorkAssignments
            .Include(a => a.AssignedToUser)
            .FirstOrDefaultAsync(a => a.AssignmentId == assignmentId, cancellationToken)
            ?? throw new NotFoundException("Work assignment not found");

        if (assignment.Status is WorkAssignmentStatuses.Completed or WorkAssignmentStatuses.Cancelled)
            throw new ConflictException("Completed or cancelled work cannot be cancelled again");

        assignment.Status = WorkAssignmentStatuses.Cancelled;
        assignment.CancelledAt = DbNow();
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(assignment);
    }

    private async Task<WorkAssignmentDto> ChangeAssignedUserStatusAsync(
        Guid assignmentId,
        Guid userId,
        string expectedStatus,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        var assignment = await _db.WorkAssignments
            .Include(a => a.AssignedToUser)
            .FirstOrDefaultAsync(a => a.AssignmentId == assignmentId, cancellationToken)
            ?? throw new NotFoundException("Work assignment not found");

        if (assignment.AssignedToUserId != userId)
            throw new ForbiddenException("This work assignment is assigned to another user");

        if (!string.Equals(assignment.Status, expectedStatus, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException($"Work assignment must be {expectedStatus} before changing to {targetStatus}");

        if (!await _permissionService.HasPermissionAsync(userId, assignment.RequiredPermissionCode, cancellationToken))
            throw new ForbiddenException("User no longer has the permission required for this work");

        assignment.Status = targetStatus;
        if (targetStatus == WorkAssignmentStatuses.InProgress)
            assignment.StartedAt = DbNow();
        if (targetStatus == WorkAssignmentStatuses.Completed)
            assignment.CompletedAt = DbNow();

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(assignment);
    }

    private static void ValidateCreateRequest(CreateWorkAssignmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskType))
            throw new ValidationException("TaskType is required");
        if (string.IsNullOrWhiteSpace(request.ReferenceType))
            throw new ValidationException("ReferenceType is required");
        if (string.IsNullOrWhiteSpace(request.ReferenceId))
            throw new ValidationException("ReferenceId is required");
        if (string.IsNullOrWhiteSpace(request.RequiredPermissionCode))
            throw new ValidationException("RequiredPermissionCode is required");
        if (request.AssignedToUserId == Guid.Empty)
            throw new ValidationException("AssignedToUserId is required");
        if (string.IsNullOrWhiteSpace(request.Priority) || !AllowedPriorities.Contains(request.Priority))
            throw new ValidationException("Priority must be LOW, NORMAL, HIGH, or URGENT");
    }

    private static WorkAssignmentDto ToDto(WorkAssignment assignment)
        => ToDto(assignment, assignment.AssignedToUser.FullName);

    private static WorkAssignmentDto ToDto(WorkAssignment assignment, string assignedToName)
        => new(
            assignment.AssignmentId,
            assignment.TaskType,
            assignment.ReferenceType,
            assignment.ReferenceId,
            assignment.RequiredPermissionCode,
            assignment.WarehouseId,
            assignment.AssignedToUserId,
            assignedToName,
            assignment.AssignedByUserId,
            assignment.Priority,
            assignment.Status,
            assignment.Note,
            assignment.AssignedAt,
            assignment.DueAt,
            assignment.StartedAt,
            assignment.CompletedAt,
            assignment.CancelledAt);

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static DateTime? ToDbDate(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : null;
}
