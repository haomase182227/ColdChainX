namespace ColdChainX.Application.DTOs.Authorization;

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string DisplayName,
    string Module,
    string? Description,
    bool IsActive,
    int SortOrder);

public sealed record RolePermissionDto(Guid Id, string Name, IReadOnlyCollection<Guid> PermissionIds);

public sealed record RolePermissionMatrixDto(
    IReadOnlyCollection<PermissionDto> Permissions,
    IReadOnlyCollection<RolePermissionDto> Roles);

public sealed class ReplaceRolePermissionsRequest
{
    public List<Guid> PermissionIds { get; set; } = new();
}

public sealed class UpsertUserPermissionRequest
{
    public string Effect { get; set; } = "ALLOW";

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }

    public string? Reason { get; set; }
}

public sealed record UserPermissionDto(
    Guid UserPermissionId,
    Guid UserId,
    Guid PermissionId,
    string PermissionCode,
    string PermissionName,
    string Effect,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    string? Reason,
    Guid GrantedBy,
    DateTime GrantedAt,
    Guid? RevokedBy,
    DateTime? RevokedAt);

public sealed record EffectivePermissionsDto(
    Guid UserId,
    string? Role,
    IReadOnlyCollection<string> PermissionCodes,
    IReadOnlyCollection<UserPermissionDto> UserOverrides);
