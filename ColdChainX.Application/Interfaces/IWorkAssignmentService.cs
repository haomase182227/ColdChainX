using ColdChainX.Application.DTOs.WorkAssignments;

namespace ColdChainX.Application.Interfaces;

public interface IWorkAssignmentService
{
    Task<WorkAssignmentDto> CreateAsync(
        CreateWorkAssignmentRequest request,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkAssignmentDto>> GetMineAsync(
        Guid userId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<WorkAssignmentDto>> GetAllAsync(
        Guid? assignedToUserId,
        Guid? warehouseId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<WorkAssignmentDto> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);

    Task<WorkAssignmentDto> StartAsync(
        Guid assignmentId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<WorkAssignmentDto> CompleteAsync(
        Guid assignmentId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<WorkAssignmentDto> CancelAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default);
}
