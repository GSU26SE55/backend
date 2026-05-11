using Microsoft.AspNetCore.Authorization;

namespace AuthService.Application.Authorization;

/// <summary>
/// Authorization attribute kiểm tra user có chứa permission code cụ thể trong JWT claim "perm".
///
/// Cách dùng:
/// <code>
/// [HasPermission(PermissionCodes.BatteryView)]
/// public IActionResult List() { ... }
/// </code>
///
/// Wire-up: trong Program.cs / DI:
/// <code>
/// services.AddSingleton&lt;IAuthorizationHandler, PermissionAuthorizationHandler&gt;();
/// services.AddSingleton&lt;IAuthorizationPolicyProvider, PermissionPolicyProvider&gt;();
/// </code>
/// </summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
    {
        Policy = PolicyPrefix + permission;
    }
}
