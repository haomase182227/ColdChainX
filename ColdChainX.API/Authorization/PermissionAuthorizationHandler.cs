using System.Security.Claims;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.API.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;

    public PermissionAuthorizationHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
            return;

        if (await _permissionService.HasPermissionAsync(userId, requirement.PermissionCode))
            context.Succeed(requirement);
    }
}
