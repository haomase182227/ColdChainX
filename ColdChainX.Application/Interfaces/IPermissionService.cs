using ColdChainX.Application.DTOs.Authorization;

namespace ColdChainX.Application.Interfaces;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default);

    Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<RolePermissionMatrixDto> GetRolePermissionMatrixAsync(CancellationToken cancellationToken = default);

    Task ReplaceRolePermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserPermissionDto>> GetUserPermissionOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserPermissionDto> UpsertUserPermissionAsync(
        Guid userId,
        Guid permissionId,
        UpsertUserPermissionRequest request,
        Guid grantedBy,
        CancellationToken cancellationToken = default);

    Task RevokeUserPermissionAsync(
        Guid userId,
        Guid permissionId,
        Guid revokedBy,
        CancellationToken cancellationToken = default);
}
