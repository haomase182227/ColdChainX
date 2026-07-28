using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.UnitTests;

public sealed class PermissionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PermissionService _service;

    public PermissionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _service = new PermissionService(_db);
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsRolePermission()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseQcInspect);
        var role = CreateRole("WarehouseWorker");
        role.Perms.Add(permission);
        var user = CreateUser(role);
        _db.AddRange(role, user);
        await _db.SaveChangesAsync();

        var allowed = await _service.HasPermissionAsync(user.UserId, permission.PermCode);

        Assert.True(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_UserDenyOverridesRolePermission()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseInventoryAdjust);
        var role = CreateRole("WarehouseWorker");
        role.Perms.Add(permission);
        var user = CreateUser(role);
        _db.AddRange(role, user, new UserPermission
        {
            UserPermissionId = Guid.NewGuid(),
            UserId = user.UserId,
            User = user,
            PermId = permission.PermId,
            Permission = permission,
            Effect = PermissionEffects.Deny,
            GrantedBy = Guid.NewGuid(),
            GrantedAt = DbNow()
        });
        await _db.SaveChangesAsync();

        var allowed = await _service.HasPermissionAsync(user.UserId, permission.PermCode);

        Assert.False(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_UserAllowAddsPermissionOutsideRole()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseLoadingConfirm);
        var role = CreateRole("WarehouseWorker");
        var user = CreateUser(role);
        _db.AddRange(role, permission, user, new UserPermission
        {
            UserPermissionId = Guid.NewGuid(),
            UserId = user.UserId,
            User = user,
            PermId = permission.PermId,
            Permission = permission,
            Effect = PermissionEffects.Allow,
            GrantedBy = Guid.NewGuid(),
            GrantedAt = DbNow(),
            ValidTo = DbNow().AddDays(1)
        });
        await _db.SaveChangesAsync();

        var allowed = await _service.HasPermissionAsync(user.UserId, permission.PermCode);

        Assert.True(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_AdminHasEveryActiveCatalogPermission()
    {
        var permission = CreatePermission(PermissionCodes.AuthorizationMatrixManage);
        var role = CreateRole("Admin");
        var user = CreateUser(role);
        _db.AddRange(permission, role, user);
        await _db.SaveChangesAsync();

        var allowed = await _service.HasPermissionAsync(user.UserId, permission.PermCode);

        Assert.True(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_AdminDeniedForUnknownPermissionCode()
    {
        var role = CreateRole("Admin");
        var user = CreateUser(role);
        _db.AddRange(role, user);
        await _db.SaveChangesAsync();

        var allowed = await _service.HasPermissionAsync(user.UserId, "UNKNOWN.PERMISSION");

        Assert.False(allowed);
    }

    [Fact]
    public async Task ReplaceRolePermissionsAsync_DoesNotAllowChangingAdmin()
    {
        var role = CreateRole("Admin");
        _db.Add(role);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReplaceRolePermissionsAsync(role.RoleId, Array.Empty<Guid>()));
    }

    [Fact]
    public async Task UpsertUserPermissionAsync_RequiresReason()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseQcInspect);
        var role = CreateRole("WarehouseWorker");
        var user = CreateUser(role);
        _db.AddRange(role, permission, user);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _service.UpsertUserPermissionAsync(
            user.UserId,
            permission.PermId,
            new UpsertUserPermissionRequest { Effect = PermissionEffects.Allow },
            Guid.NewGuid()));
    }

    public void Dispose() => _db.Dispose();

    private static Permission CreatePermission(string code) => new()
    {
        PermId = Guid.NewGuid(),
        PermCode = code,
        Module = "WAREHOUSE",
        DisplayName = code,
        IsActive = true
    };

    private static Role CreateRole(string name) => new()
    {
        RoleId = Guid.NewGuid(),
        RoleName = name
    };

    private static User CreateUser(Role role) => new()
    {
        UserId = Guid.NewGuid(),
        Username = Guid.NewGuid().ToString("N"),
        FullName = "Permission Test User",
        RoleId = role.RoleId,
        Role = role,
        Status = "ACTIVE"
    };

    private static DateTime DbNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
