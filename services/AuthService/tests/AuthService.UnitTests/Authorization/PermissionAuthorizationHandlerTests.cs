using System.Security.Claims;
using AuthService.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace AuthService.UnitTests.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext BuildContext(
        PermissionRequirement requirement,
        bool authenticated,
        params string[] permissionClaims)
    {
        var identity = new ClaimsIdentity(
            permissionClaims.Select(p => new Claim(PermissionAuthorizationHandler.PermissionClaimType, p)),
            authenticated ? "TestAuth" : null);
        var user = new ClaimsPrincipal(identity);
        return new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
    }

    [Fact]
    public async Task Succeed_When_UserHasMatchingPermissionClaim()
    {
        var requirement = new PermissionRequirement(PermissionCodes.BatteryView);
        var ctx = BuildContext(requirement, authenticated: true,
            PermissionCodes.BatteryView, PermissionCodes.TicketView);

        var handler = new PermissionAuthorizationHandler();
        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Fail_When_UserDoesNotHavePermission()
    {
        var requirement = new PermissionRequirement(PermissionCodes.UserDelete);
        var ctx = BuildContext(requirement, authenticated: true, PermissionCodes.BatteryView);

        var handler = new PermissionAuthorizationHandler();
        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Fail_When_UserNotAuthenticated()
    {
        var requirement = new PermissionRequirement(PermissionCodes.BatteryView);
        var ctx = BuildContext(requirement, authenticated: false, PermissionCodes.BatteryView);

        var handler = new PermissionAuthorizationHandler();
        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Succeed_Case_Insensitive_PermissionMatching()
    {
        var requirement = new PermissionRequirement("BATTERY.VIEW");
        var ctx = BuildContext(requirement, authenticated: true, "battery.view");

        var handler = new PermissionAuthorizationHandler();
        await handler.HandleAsync(ctx);

        ctx.HasSucceeded.Should().BeTrue();
    }
}

public class PermissionPolicyProviderTests
{
    [Fact]
    public async Task GetPolicy_PermPrefix_ReturnsPolicyWithRequirement()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(options);

        var policy = await provider.GetPolicyAsync($"{HasPermissionAttribute.PolicyPrefix}{PermissionCodes.TicketAssign}");

        policy.Should().NotBeNull();
        policy!.Requirements.OfType<PermissionRequirement>()
            .Should().ContainSingle(r => r.Permission == PermissionCodes.TicketAssign);
    }

    [Fact]
    public async Task GetPolicy_OtherName_FallsBack()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(options);

        var policy = await provider.GetPolicyAsync("SomeOtherPolicy");

        policy.Should().BeNull(); // default provider returns null when policy chưa register
    }
}

public class HasPermissionAttributeTests
{
    [Fact]
    public void Attribute_PolicyName_FollowsPattern()
    {
        var attr = new HasPermissionAttribute(PermissionCodes.BatteryView);
        attr.Policy.Should().Be($"{HasPermissionAttribute.PolicyPrefix}{PermissionCodes.BatteryView}");
    }
}
