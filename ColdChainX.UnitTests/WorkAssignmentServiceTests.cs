using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Application.DTOs.WorkAssignments;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.UnitTests;

public sealed class WorkAssignmentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly PermissionService _permissionService;
    private readonly WorkAssignmentService _service;

    public WorkAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _permissionService = new PermissionService(_db);
        _service = new WorkAssignmentService(_db, _permissionService);
    }

    [Fact]
    public async Task CreateAsync_RejectsAssigneeWithoutRequiredPermission()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseQcInspect);
        var role = CreateRole();
        var user = CreateUser(role);
        _db.AddRange(role, permission, user);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(
            CreateRequest(user.UserId, permission.PermCode),
            Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAndCompleteAsync_RequiresAssignedUserAndPermission()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseQcInspect);
        var role = CreateRole();
        var user = CreateUser(role);
        _db.AddRange(role, permission, user);
        await _db.SaveChangesAsync();
        await _permissionService.UpsertUserPermissionAsync(
            user.UserId,
            permission.PermId,
            new UpsertUserPermissionRequest
            {
                Effect = PermissionEffects.Allow,
                Reason = "Temporary QC assignment"
            },
            Guid.NewGuid());

        var created = await _service.CreateAsync(
            CreateRequest(user.UserId, permission.PermCode),
            Guid.NewGuid());

        var started = await _service.StartAsync(created.AssignmentId, user.UserId);
        var completed = await _service.CompleteAsync(created.AssignmentId, user.UserId);

        Assert.Equal(WorkAssignmentStatuses.Assigned, created.Status);
        Assert.Equal(WorkAssignmentStatuses.InProgress, started.Status);
        Assert.Equal(WorkAssignmentStatuses.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task StartAsync_RejectsAnotherUser()
    {
        var permission = CreatePermission(PermissionCodes.WarehouseLoadingConfirm);
        var role = CreateRole();
        role.Perms.Add(permission);
        var assignedUser = CreateUser(role);
        var anotherUser = CreateUser(role);
        _db.AddRange(role, assignedUser, anotherUser);
        await _db.SaveChangesAsync();

        var created = await _service.CreateAsync(
            CreateRequest(assignedUser.UserId, permission.PermCode),
            Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.StartAsync(
            created.AssignmentId,
            anotherUser.UserId));
    }

    public void Dispose() => _db.Dispose();

    private static CreateWorkAssignmentRequest CreateRequest(Guid userId, string permissionCode) => new()
    {
        TaskType = "QC_INSPECTION",
        ReferenceType = "WAREHOUSE_RECEIPT",
        ReferenceId = Guid.NewGuid().ToString(),
        RequiredPermissionCode = permissionCode,
        AssignedToUserId = userId,
        Priority = "NORMAL"
    };

    private static Permission CreatePermission(string code) => new()
    {
        PermId = Guid.NewGuid(),
        PermCode = code,
        Module = "WAREHOUSE",
        DisplayName = code,
        IsActive = true
    };

    private static Role CreateRole() => new()
    {
        RoleId = Guid.NewGuid(),
        RoleName = "WarehouseWorker"
    };

    private static User CreateUser(Role role) => new()
    {
        UserId = Guid.NewGuid(),
        Username = Guid.NewGuid().ToString("N"),
        FullName = "Work Assignment Test User",
        RoleId = role.RoleId,
        Role = role,
        Status = "ACTIVE"
    };
}
