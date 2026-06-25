using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;

namespace TicketService.IntergrationTests.Fixtures;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string UserId = "00000000-0000-0000-0000-000000000001";
    public const string UserName = "TestUser";

    /// <summary>
    /// Header dùng để override role claim cho 1 request cụ thể (vd: test case 403 Forbidden
    /// cho role không đủ quyền). Giá trị: danh sách role cách nhau bởi dấu phẩy (vd: "Customer,Staff").
    /// Nếu request không gửi header này → giữ behavior cũ, gán đủ cả 4 role.
    /// </summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>
    /// Mapping role → permission code mặc định, mirror đúng <c>PermissionSeed.RoleDefaults</c>
    /// (AuthService) phần Chat — chỉ cần thiết để các handler dùng <c>HasPermission()</c>/
    /// <c>UserPermissions</c> (Sprint Chat Phase 2 #516) hoạt động đúng trong integration test,
    /// vì JWT thật cấp claim "perm" theo seed này, còn TestAuthHandler giả lập claim thủ công.
    /// </summary>
    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["Admin"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatEditAny,
            ChatPermissionCodes.ChatDeleteOwn, ChatPermissionCodes.ChatDeleteAny,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal,
            ChatPermissionCodes.ChatTemplateCreateGlobal
        },
        ["Manager"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatEditAny,
            ChatPermissionCodes.ChatDeleteOwn, ChatPermissionCodes.ChatDeleteAny,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal,
            ChatPermissionCodes.ChatTemplateCreateGlobal
        },
        ["Staff"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatDeleteOwn,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal
        },
        ["Customer"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatEditOwn,
            ChatPermissionCodes.ChatDeleteOwn
        }
    };

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var roles = Request.Headers.TryGetValue(RolesHeader, out var headerValue)
            ? headerValue.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "Customer", "Staff", "Manager", "Admin" };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserId),
            new(ClaimTypes.Name, UserName)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Permission claims theo role ĐẦU TIÊN trong mảng — khớp với cách
        // TicketCurrentUserService.Role (FindFirstValue) resolve "role chính" của actor.
        // Không union tất cả role được cấp, tránh actor có permission "any" giả tạo
        // trong lúc role resolve ra "Customer" (gây sai lệch giữa role-check và permission-check).
        if (roles.Length > 0 && RolePermissions.TryGetValue(roles[0], out var permissions))
            claims.AddRange(permissions.Select(permission => new Claim("perm", permission)));

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
