using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BatteryService.Api.Authentication;

/// <summary>
/// Sprint IoT-1 (#243) — chấp nhận đồng thời:
/// 1. <b>Per-device API key</b>: header <c>X-Api-Key</c> (raw <c>iotk_...</c>) + <c>X-Device-Code</c>. Scope từ <see cref="IotApiKeyScopeEnum"/>.
/// 2. <b>Legacy global key</b>: header <c>X-Api-Key</c> khớp <c>ApiKeys:SensorIngest</c>. Giữ cho simulator/MVP (§52bis.3).
///
/// Endpoint xác định scope cần thiết bằng <see cref="IotApiKeyScopeRequirementAttribute"/>.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string DeviceCodeHeader = "X-Device-Code";

    public const string ClaimDeviceId = "iot:device_id";
    public const string ClaimDeviceCode = "iot:device_code";
    public const string ClaimDeviceSiteId = "iot:site_id";
    public const string ClaimScopes = "iot:scopes";

    /// <summary>
    /// GH-785 — cờ đánh dấu "khoá HỢP LỆ nhưng thiếu scope", để bước challenge trả 403 thay vì 401.
    /// </summary>
    private const string ScopeDeniedItemKey = "iot:scope_denied";

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var providedApiKeys))
            return AuthenticateResult.Fail("Missing API key.");

        var providedKey = providedApiKeys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey))
            return AuthenticateResult.Fail("Missing API key.");

        if (providedKey.StartsWith("iotk_", StringComparison.Ordinal))
        {
            using var scope = _serviceProvider.CreateScope();
            var apiKeyService = scope.ServiceProvider.GetRequiredService<IIotApiKeyService>();
            var requiredScope = ResolveRequiredScope();
            var lookup = await apiKeyService.LookupDeviceByRawKeyAsync(providedKey, requiredScope, Context.RequestAborted);

            // GH-785 — tách hai ca vốn bị gộp thành 401: khoá sai (401 — "chưa biết anh là ai") và
            // khoá đúng nhưng thiếu quyền (403 — "biết anh là ai, nhưng không được phép"). Gộp lại
            // khiến người vận hành đi xoay khoá mãi mà không nhận ra vấn đề thật là thiếu scope.
            if (lookup.ScopeDenied)
            {
                Context.Items[ScopeDeniedItemKey] = true;
                return AuthenticateResult.Fail("Device API key thiếu scope cho endpoint này.");
            }

            var device = lookup.Device;
            if (device is null)
                return AuthenticateResult.Fail("Invalid or unauthorized device API key.");

            if (Request.Headers.TryGetValue(DeviceCodeHeader, out var deviceCodeHeader))
            {
                var headerCode = deviceCodeHeader.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(headerCode) && !string.Equals(headerCode, device.DeviceCode, StringComparison.Ordinal))
                    return AuthenticateResult.Fail("Device code mismatch.");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
                new Claim(ClaimTypes.Name, device.DisplayName),
                new Claim(ClaimTypes.Role, "IotDevice"),
                new Claim(ClaimDeviceId, device.Id.ToString()),
                new Claim(ClaimDeviceCode, device.DeviceCode),
                new Claim(ClaimDeviceSiteId, device.SiteId.ToString()),
                new Claim(ClaimScopes, ((int)device.ApiKeyScopes).ToString())
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }

        var expectedApiKey = _configuration["ApiKeys:SensorIngest"]
                             ?? _configuration["BATTERY_SENSOR_INGEST_API_KEY"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return AuthenticateResult.Fail("Sensor ingest API key is not configured.");

        if (!SecureEquals(providedKey, expectedApiKey))
            return AuthenticateResult.Fail("Invalid API key.");

        var legacyClaims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "iot-gateway"),
            new Claim(ClaimTypes.Name, "IoT Gateway (legacy)"),
            new Claim(ClaimTypes.Role, "System")
        };

        var legacyIdentity = new ClaimsIdentity(legacyClaims, SchemeName);
        var legacyPrincipal = new ClaimsPrincipal(legacyIdentity);
        return AuthenticateResult.Success(new AuthenticationTicket(legacyPrincipal, SchemeName));
    }

    private IotApiKeyScopeEnum ResolveRequiredScope()
    {
        var endpoint = Context.GetEndpoint();
        var attr = endpoint?.Metadata.GetMetadata<IotApiKeyScopeRequirementAttribute>();
        return attr?.RequiredScope ?? IotApiKeyScopeEnum.SensorIngest;
    }

    private static bool SecureEquals(string providedApiKey, string expectedApiKey)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    /// <summary>
    /// GH-785 — trả 403 cho ca thiếu scope, 401 cho mọi ca còn lại.
    /// </summary>
    /// <remarks>
    /// Phải làm ở đây vì scope được kiểm ngay trong bước xác thực (khoá thiết bị mang luôn quyền),
    /// nên ASP.NET mặc định biến mọi thất bại thành 401 qua challenge. Không có nhánh này thì hợp
    /// đồng API nói 403 còn hệ thống trả 401 — tài liệu và hành vi nói hai chuyện khác nhau.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.TryGetValue(ScopeDeniedItemKey, out var denied) && denied is true)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return base.HandleChallengeAsync(properties);
    }

}

/// <summary>
/// Sprint IoT-1 (#243) — gắn vào controller action để declare scope cần kiểm tra
/// đối với per-device key. Ignored với legacy global key.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class IotApiKeyScopeRequirementAttribute : Attribute
{
    public IotApiKeyScopeEnum RequiredScope { get; }
    public IotApiKeyScopeRequirementAttribute(IotApiKeyScopeEnum scope) => RequiredScope = scope;
}
