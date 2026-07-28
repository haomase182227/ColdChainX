using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.API.Authorization;

public sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;
