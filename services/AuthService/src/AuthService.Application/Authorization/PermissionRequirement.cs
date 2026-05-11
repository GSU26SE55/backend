using Microsoft.AspNetCore.Authorization;

namespace AuthService.Application.Authorization;

/// <summary>
/// Requirement gắn với <see cref="HasPermissionAttribute"/>. Mỗi instance đại diện cho 1 permission code.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
