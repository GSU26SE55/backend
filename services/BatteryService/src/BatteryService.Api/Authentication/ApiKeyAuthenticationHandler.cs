using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BatteryService.Api.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedApiKey = _configuration["ApiKeys:SensorIngest"]
                             ?? _configuration["BATTERY_SENSOR_INGEST_API_KEY"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Sensor ingest API key is not configured."));

        if (!Request.Headers.TryGetValue(HeaderName, out var providedApiKeys))
            return Task.FromResult(AuthenticateResult.Fail("Missing sensor ingest API key."));

        var providedApiKey = providedApiKeys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey) || !SecureEquals(providedApiKey, expectedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid sensor ingest API key."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "iot-gateway"),
            new Claim(ClaimTypes.Name, "IoT Gateway"),
            new Claim(ClaimTypes.Role, "System")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool SecureEquals(string providedApiKey, string expectedApiKey)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
