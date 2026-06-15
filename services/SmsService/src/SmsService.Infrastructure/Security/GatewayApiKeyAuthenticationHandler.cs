using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsService.Application.Interfaces.Services;
using SmsService.Infrastructure.Persistence;

namespace SmsService.Infrastructure.Security;

public class GatewayAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "GatewayApiKey";
}

/// <summary>
/// Auth scheme dành riêng cho Flutter app — KHÔNG tái dùng JWT user.
/// <list type="bullet">
///   <item>REST: header <c>X-Device-Code</c> + <c>Authorization: Bearer &lt;api-key-plaintext&gt;</c></item>
///   <item>SignalR WebSocket: query <c>deviceCode</c> + <c>access_token</c></item>
/// </list>
/// Set claim <c>device_code</c>, <c>device_id</c> để controller / Hub dùng.
/// </summary>
public class GatewayApiKeyAuthenticationHandler : AuthenticationHandler<GatewayAuthOptions>
{
    private readonly SmsDbContext _db;
    private readonly IGatewayApiKeyHasher _hasher;

    public GatewayApiKeyAuthenticationHandler(
        IOptionsMonitor<GatewayAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        SmsDbContext db,
        IGatewayApiKeyHasher hasher) : base(options, logger, encoder)
    {
        _db = db;
        _hasher = hasher;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Device code: header (REST) hoặc query (WS).
        var deviceCode = Request.Headers["X-Device-Code"].ToString();
        if (string.IsNullOrWhiteSpace(deviceCode))
            deviceCode = Request.Query["deviceCode"].ToString();
        if (string.IsNullOrWhiteSpace(deviceCode))
            return AuthenticateResult.Fail("Missing X-Device-Code / deviceCode.");

        // Token: header Bearer (REST) hoặc query access_token (WS).
        string apiKey;
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            apiKey = auth["Bearer ".Length..].Trim();
        }
        else
        {
            apiKey = Request.Query["access_token"].ToString();
            if (string.IsNullOrWhiteSpace(apiKey))
                return AuthenticateResult.Fail("Missing Bearer token / access_token.");
        }

        var device = await _db.SmsGatewayDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DeviceCode == deviceCode && x.IsActive && !x.IsDeleted);
        if (device is null)
            return AuthenticateResult.Fail("Unknown or revoked device.");

        if (!_hasher.Verify(apiKey, device.ApiKeyHash))
            return AuthenticateResult.Fail("Invalid api key.");

        var claims = new[]
        {
            new Claim("device_code", device.DeviceCode),
            new Claim("device_id",   device.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
            new Claim(ClaimTypes.Name, device.DeviceName),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
