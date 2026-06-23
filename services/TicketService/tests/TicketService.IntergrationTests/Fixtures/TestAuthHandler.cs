using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
