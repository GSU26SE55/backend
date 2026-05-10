using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using SharedInfrastructure.DependencyInjection.Extensions;

namespace SharedInfrastructure.UnitTests.DependencyInjection;

/// <summary>
/// Test AddJwtAuthentication: registers JwtBearer scheme với custom OnChallenge/OnForbidden events
/// trả JSON response thay vì empty body. Boot TestServer mini để verify HTTP behavior thật.
/// </summary>
public class JwtAuthenticationTests
{
    private const string SecretKey = "test-jwt-secret-key-must-be-at-least-32-chars-long-1234567";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private static IHostBuilder BuildHost(Action<WebHostBuilderContext, IServiceCollection>? extra = null)
    {
        return new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer()
                .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:SecretKey"] = SecretKey,
                    ["JwtSettings:Issuer"] = Issuer,
                    ["JwtSettings:Audience"] = Audience
                }))
                .ConfigureServices((ctx, services) =>
                {
                    services.AddJwtAuthentication(ctx.Configuration);
                    services.AddAuthorization();
                    services.AddRouting();
                    extra?.Invoke(ctx, services);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", [Authorize] (HttpContext _) => Results.Ok("ok"));
                        endpoints.MapGet("/admin", [Authorize(Policy = "AdminOnly")] (HttpContext _) => Results.Ok("admin"));
                    });
                });
        });
    }

    private static string MakeToken(string secret = SecretKey, string issuer = Issuer, string audience = Audience,
        DateTime? expires = null, IEnumerable<Claim>? extraClaims = null)
    {
        var key = Encoding.ASCII.GetBytes(secret);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "u@x.com")
        };
        if (extraClaims != null)
            claims.AddRange(extraClaims);

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expires ?? DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Fact]
    public void AddJwtAuthentication_RegistersJwtBearerScheme()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = SecretKey,
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience
        }).Build();

        services.AddJwtAuthentication(config);

        var sp = services.BuildServiceProvider();
        sp.GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>()
            .Should().NotBeNull();
    }

    [Fact]
    public async Task NoAuthHeader_Returns401_WithMissingTokenErrorCode()
    {
        using var host = await BuildHost().StartAsync();

        var resp = await host.GetTestClient().GetAsync("/protected");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(401);
        doc.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("MISSING_TOKEN");
    }

    [Fact]
    public async Task ExpiredToken_Returns401_TokenExpiredErrorCode_AndHeader()
    {
        using var host = await BuildHost().StartAsync();
        var token = MakeToken(expires: DateTime.UtcNow.AddMinutes(-1));

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resp.Headers.Contains("Token-Expired").Should().BeTrue();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("TOKEN_EXPIRED");
    }

    [Fact]
    public async Task TokenSignedWithDifferentKey_Returns401_InvalidSignatureErrorCode()
    {
        using var host = await BuildHost().StartAsync();
        var token = MakeToken(secret: "completely-different-key-must-be-32-chars-long-or-more-12345");

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("INVALID_SIGNATURE");
    }

    [Fact]
    public async Task ValidToken_AccessGranted()
    {
        using var host = await BuildHost().StartAsync();
        var token = MakeToken();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedButForbidden_Returns403_WithCustomJsonShape()
    {
        // Add policy "AdminOnly" requires Role=1.
        using var host = await BuildHost((_, services) =>
        {
            services.AddAuthorization(options =>
                options.AddPolicy("AdminOnly", p => p.RequireClaim("Role", "1")));
        }).StartAsync();

        // Token không có Role=1 claim.
        var token = MakeToken(extraClaims: new[] { new Claim("Role", "2") });
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/admin");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("message").GetString().Should().Contain("not allowed");
    }
}
