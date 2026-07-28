namespace ColdChainX.Core.Entities;

public class WorkAssignment
{
    public Guid AssignmentId { get; set; }

    public string TaskType { get; set; } = null!;

    public string ReferenceType { get; set; } = null!;

    public string ReferenceId { get; set; } = null!;

    public string RequiredPermissionCode { get; set; } = null!;

    public Guid? WarehouseId { get; set; }

    public Guid AssignedToUserId { get; set; }

    public Guid AssignedByUserId { get; set; }

    public string Priority { get; set; } = "NORMAL";

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime AssignedAt { get; set; }

    public DateTime? DueAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public virtual User AssignedToUser { get; set; } = null!;

    public virtual Warehouse? Warehouse { get; set; }
}
