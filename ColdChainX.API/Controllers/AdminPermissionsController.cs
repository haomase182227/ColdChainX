using System.Security.Claims;
using ColdChainX.API.Authorization;
using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Constants;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/admin/permissions")]
[Authorize]
public sealed class AdminPermissionsController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public AdminPermissionsController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet("matrix")]
    [HasPermission(PermissionCodes.AuthorizationMatrixView)]
    public async Task<ActionResult<ApiResponse<RolePermissionMatrixDto>>> GetMatrix(
        CancellationToken cancellationToken)
    {
        var result = await _permissionService.GetRolePermissionMatrixAsync(cancellationToken);
        return Ok(ApiResponse<RolePermissionMatrixDto>.SuccessResponse(
            result,
            "Permission matrix retrieved successfully"));
    }

    [HttpPut("roles/{roleId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(PermissionCodes.AuthorizationMatrixManage)]
    public async Task<ActionResult<ApiResponse<bool>>> ReplaceRolePermissions(
        Guid roleId,
        [FromBody] ReplaceRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        await _permissionService.ReplaceRolePermissionsAsync(
            roleId,
            request.PermissionIds,
            cancellationToken);

        return Ok(ApiResponse<bool>.SuccessResponse(true, "Role permissions updated successfully"));
    }

    [HttpGet("users/{userId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(PermissionCodes.AuthorizationMatrixManage)]
    public async Task<ActionResult<ApiResponse<EffectivePermissionsDto>>> GetUserPermissions(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _permissionService.GetEffectivePermissionsAsync(userId, cancellationToken);
        return Ok(ApiResponse<EffectivePermissionsDto>.SuccessResponse(
            result,
            "User permissions retrieved successfully"));
    }

    [HttpPut("users/{userId:guid}/{permissionId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(PermissionCodes.AuthorizationMatrixManage)]
    public async Task<ActionResult<ApiResponse<UserPermissionDto>>> UpsertUserPermission(
        Guid userId,
        Guid permissionId,
        [FromBody] UpsertUserPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _permissionService.UpsertUserPermissionAsync(
            userId,
            permissionId,
            request,
            GetCurrentUserId(),
            cancellationToken);

        return Ok(ApiResponse<UserPermissionDto>.SuccessResponse(
            result,
            "User permission override saved successfully"));
    }

    [HttpDelete("users/{userId:guid}/{permissionId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [HasPermission(PermissionCodes.AuthorizationMatrixManage)]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeUserPermission(
        Guid userId,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        await _permissionService.RevokeUserPermissionAsync(
            userId,
            permissionId,
            GetCurrentUserId(),
            cancellationToken);

        return Ok(ApiResponse<bool>.SuccessResponse(true, "User permission override revoked successfully"));
    }

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}
