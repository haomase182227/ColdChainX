namespace ColdChainX.Application.DTOs.WorkAssignments;

public sealed class CreateWorkAssignmentRequest
{
    public string TaskType { get; set; } = null!;

    public string ReferenceType { get; set; } = null!;

    public string ReferenceId { get; set; } = null!;

    public string RequiredPermissionCode { get; set; } = null!;

    public Guid? WarehouseId { get; set; }

    public Guid AssignedToUserId { get; set; }

    public string Priority { get; set; } = "NORMAL";

    public DateTime? DueAt { get; set; }

    public string? Note { get; set; }
}

public sealed record WorkAssignmentDto(
    Guid AssignmentId,
    string TaskType,
    string ReferenceType,
    string ReferenceId,
    string RequiredPermissionCode,
    Guid? WarehouseId,
    Guid AssignedToUserId,
    string AssignedToName,
    Guid AssignedByUserId,
    string Priority,
    string Status,
    string? Note,
    DateTime AssignedAt,
    DateTime? DueAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt);
