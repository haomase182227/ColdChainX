using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;

    public PermissionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        var normalizedCode = permissionCode.Trim().ToUpperInvariant();
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (user?.Role == null || string.Equals(user.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(user.Role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return await _db.Permissions
                .AsNoTracking()
                .AnyAsync(
                    p => p.IsActive && p.PermCode == normalizedCode,
                    cancellationToken);
        }

        var now = DbNow();
        var userOverride = await _db.UserPermissions
            .AsNoTracking()
            .Include(up => up.Permission)
            .FirstOrDefaultAsync(
                up => up.UserId == userId
                      && up.Permission.PermCode == normalizedCode
                      && up.Permission.IsActive
                      && up.RevokedAt == null
                      && (up.ValidFrom == null || up.ValidFrom <= now)
                      && (up.ValidTo == null || up.ValidTo >= now),
                cancellationToken);

        if (userOverride != null)
            return string.Equals(userOverride.Effect, PermissionEffects.Allow, StringComparison.OrdinalIgnoreCase);

        return await _db.Roles
            .AsNoTracking()
            .Where(r => r.RoleId == user.RoleId)
            .SelectMany(r => r.Perms)
            .AnyAsync(
                p => p.IsActive && p.PermCode == normalizedCode,
                cancellationToken);
    }

    public async Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("User not found");

        var overrides = await GetUserPermissionOverridesAsync(userId, cancellationToken);
        var now = DbNow();

        HashSet<string> codes;
        if (string.Equals(user.Role?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            codes = (await _db.Permissions
                    .AsNoTracking()
                    .Where(p => p.IsActive)
                    .Select(p => p.PermCode)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else if (user.RoleId.HasValue)
        {
            codes = (await _db.Roles
                    .AsNoTracking()
                    .Where(r => r.RoleId == user.RoleId.Value)
                    .SelectMany(r => r.Perms)
                    .Where(p => p.IsActive)
                    .Select(p => p.PermCode)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.Equals(user.Role?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in overrides.Where(item =>
                         item.RevokedAt == null
                         && (!item.ValidFrom.HasValue || item.ValidFrom <= now)
                         && (!item.ValidTo.HasValue || item.ValidTo >= now)))
            {
                if (string.Equals(item.Effect, PermissionEffects.Deny, StringComparison.OrdinalIgnoreCase))
                    codes.Remove(item.PermissionCode);
                else
                    codes.Add(item.PermissionCode);
            }
        }

        return new EffectivePermissionsDto(
            user.UserId,
            user.Role?.RoleName,
            codes.OrderBy(code => code).ToArray(),
            overrides);
    }

    public async Task<RolePermissionMatrixDto> GetRolePermissionMatrixAsync(
        CancellationToken cancellationToken = default)
    {
        var permissions = await _db.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.PermCode)
            .Select(p => new PermissionDto(
                p.PermId,
                p.PermCode,
                p.DisplayName,
                p.Module,
                p.Description,
                p.IsActive,
                p.SortOrder))
            .ToListAsync(cancellationToken);

        var roles = await _db.Roles
            .AsNoTracking()
            .Include(r => r.Perms)
            .OrderBy(r => r.RoleName)
            .ToListAsync(cancellationToken);

        var activePermissionIds = permissions
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Id)
            .ToArray();

        return new RolePermissionMatrixDto(
            permissions,
            roles.Select(role => new RolePermissionDto(
                    role.RoleId,
                    role.RoleName,
                    string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase)
                        ? activePermissionIds
                        : role.Perms.Select(permission => permission.PermId).ToArray()))
                .ToArray());
    }

    public async Task ReplaceRolePermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<Guid> permissionIds,
        CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles
            .Include(r => r.Perms)
            .FirstOrDefaultAsync(r => r.RoleId == roleId, cancellationToken)
            ?? throw new NotFoundException("Role not found");

        if (string.Equals(role.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Admin always has every active permission and cannot be changed");

        var uniquePermissionIds = permissionIds.Distinct().ToArray();
        var permissions = await _db.Permissions
            .Where(p => uniquePermissionIds.Contains(p.PermId) && p.IsActive)
            .ToListAsync(cancellationToken);

        if (permissions.Count != uniquePermissionIds.Length)
            throw new ValidationException("One or more permissions do not exist or are inactive");

        role.Perms.Clear();
        foreach (var permission in permissions)
            role.Perms.Add(permission);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserPermissionDto>> GetUserPermissionOverridesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == userId, cancellationToken);
        if (!userExists)
            throw new NotFoundException("User not found");

        var items = await _db.UserPermissions
            .AsNoTracking()
            .Include(up => up.Permission)
            .Where(up => up.UserId == userId)
            .OrderBy(up => up.Permission.Module)
            .ThenBy(up => up.Permission.PermCode)
            .ToListAsync(cancellationToken);

        return items.Select(ToDto).ToArray();
    }

    public async Task<UserPermissionDto> UpsertUserPermissionAsync(
        Guid userId,
        Guid permissionId,
        UpsertUserPermissionRequest request,
        Guid grantedBy,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionEffects.IsValid(request.Effect))
            throw new ValidationException("Effect must be ALLOW or DENY");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("Reason is required for a user permission override");

        var validFrom = ToDbDate(request.ValidFrom);
        var validTo = ToDbDate(request.ValidTo);
        if (validFrom.HasValue && validTo.HasValue && validTo <= validFrom)
            throw new ValidationException("ValidTo must be later than ValidFrom");

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("User not found");

        if (string.Equals(user.Role?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Admin permissions are fixed and cannot be overridden");

        var permission = await _db.Permissions
            .FirstOrDefaultAsync(p => p.PermId == permissionId && p.IsActive, cancellationToken)
            ?? throw new NotFoundException("Permission not found or inactive");

        var userPermission = await _db.UserPermissions
            .Include(up => up.Permission)
            .FirstOrDefaultAsync(
                up => up.UserId == userId && up.PermId == permissionId,
                cancellationToken);

        if (userPermission == null)
        {
            userPermission = new UserPermission
            {
                UserPermissionId = Guid.NewGuid(),
                UserId = userId,
                PermId = permissionId,
                Permission = permission
            };
            _db.UserPermissions.Add(userPermission);
        }

        userPermission.Effect = PermissionEffects.Normalize(request.Effect);
        userPermission.ValidFrom = validFrom;
        userPermission.ValidTo = validTo;
        userPermission.Reason = request.Reason.Trim();
        userPermission.GrantedBy = grantedBy;
        userPermission.GrantedAt = DbNow();
        userPermission.RevokedBy = null;
        userPermission.RevokedAt = null;

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(userPermission);
    }

    public async Task RevokeUserPermissionAsync(
        Guid userId,
        Guid permissionId,
        Guid revokedBy,
        CancellationToken cancellationToken = default)
    {
        var userPermission = await _db.UserPermissions
            .FirstOrDefaultAsync(
                up => up.UserId == userId && up.PermId == permissionId && up.RevokedAt == null,
                cancellationToken)
            ?? throw new NotFoundException("Active user permission override not found");

        userPermission.RevokedBy = revokedBy;
        userPermission.RevokedAt = DbNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static UserPermissionDto ToDto(UserPermission item)
        => new(
            item.UserPermissionId,
            item.UserId,
            item.PermId,
            item.Permission.PermCode,
            item.Permission.DisplayName,
            item.Effect,
            item.ValidFrom,
            item.ValidTo,
            item.Reason,
            item.GrantedBy,
            item.GrantedAt,
            item.RevokedBy,
            item.RevokedAt);

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static DateTime? ToDbDate(DateTime? value)
        => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : null;
}
