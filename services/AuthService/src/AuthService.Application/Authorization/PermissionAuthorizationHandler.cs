using Microsoft.AspNetCore.Authorization;

namespace AuthService.Application.Authorization;

/// <summary>
/// Handler: succeed nếu user có claim "perm" với value khớp <see cref="PermissionRequirement.Permission"/>.
/// Case-insensitive compare.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string PermissionClaimType = "perm";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var hasPermission = context.User.Claims.Any(c =>
            string.Equals(c.Type, PermissionClaimType, StringComparison.Ordinal)
            && string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (hasPermission)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
