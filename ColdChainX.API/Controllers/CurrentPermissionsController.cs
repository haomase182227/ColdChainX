using System.Security.Claims;
using ColdChainX.Application.DTOs.Authorization;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.API.Controllers;

[ApiController]
[Route("api/auth/me/permissions")]
[Authorize]
public sealed class CurrentPermissionsController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public CurrentPermissionsController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<EffectivePermissionsDto>>> Get(
        CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(value, out var userId))
            return Unauthorized(ApiResponse<object>.Failure("Invalid user identity", StatusCodes.Status401Unauthorized));

        var result = await _permissionService.GetEffectivePermissionsAsync(userId, cancellationToken);
        return Ok(ApiResponse<EffectivePermissionsDto>.SuccessResponse(
            result,
            "Effective permissions retrieved successfully"));
    }
}
