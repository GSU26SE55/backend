using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedInfrastructure.RateLimiting;
using Xunit;

namespace SharedInfrastructure.UnitTests.RateLimiting;

/// <summary>
/// Chạy limiter qua pipeline HTTP thật (TestServer) để khẳng định đúng con số request thứ mấy bị chặn,
/// chứ không chỉ khẳng định cấu hình.
/// </summary>
public class StandardRateLimiterPipelineTests
{
    [Fact]
    public async Task Anonymous_IsBlockedOn61stRequest()
    {
        using var host = await BuildHostAsync(authenticated: false);
        var client = host.GetTestClient();

        var statuses = await FireAsync(client, "/api/ping", 62);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(60);
        statuses.Take(60).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses[60].Should().Be(HttpStatusCode.TooManyRequests);
        statuses[61].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Authenticated_IsBlockedOn501stRequest()
    {
        using var host = await BuildHostAsync(authenticated: true);
        var client = host.GetTestClient();

        var statuses = await FireAsync(client, "/api/ping", 502);

        statuses.Count(s => s == HttpStatusCode.OK).Should().Be(500);
        statuses.Take(500).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses[500].Should().Be(HttpStatusCode.TooManyRequests);
        statuses[501].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ExemptPaths_AreNeverBlocked()
    {
        using var host = await BuildHostAsync(authenticated: false);
        var client = host.GetTestClient();

        var statuses = await FireAsync(client, "/health", 120);

        statuses.Should().OnlyContain(s => s == HttpStatusCode.OK,
            "health check phải sống sót qua cả một đợt traffic ẩn danh, nếu không container sẽ bị restart");
    }

    [Fact]
    public async Task Rejection_ReturnsCommonResponseShape_WithRetryAfterHeader()
    {
        using var host = await BuildHostAsync(authenticated: false);
        var client = host.GetTestClient();

        await FireAsync(client, "/api/ping", 60);
        var response = await client.GetAsync("/api/ping");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        retryAfter!.Single().Should().Be("30");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        body.GetProperty("statusCode").GetInt32().Should().Be(429);
        body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("RATE_LIMITED");
        body.GetProperty("data").GetProperty("retryAfterSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task Disabled_LetsEverythingThrough()
    {
        using var host = await BuildHostAsync(authenticated: false, enabled: false);
        var client = host.GetTestClient();

        var statuses = await FireAsync(client, "/api/ping", 100);

        statuses.Should().OnlyContain(s => s == HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfiguredLimits_OverrideDefaults()
    {
        using var host = await BuildHostAsync(
            authenticated: false,
            settings: new Dictionary<string, string?>
            {
                ["RateLimiting:AnonymousPermitLimit"] = "3",
                ["RateLimiting:WindowSeconds"] = "60"
            });
        var client = host.GetTestClient();

        var statuses = await FireAsync(client, "/api/ping", 4);

        statuses.Take(3).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses[3].Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static async Task<List<HttpStatusCode>> FireAsync(HttpClient client, string path, int count)
    {
        var statuses = new List<HttpStatusCode>(count);
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(path);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static async Task<IHost> BuildHostAsync(
        bool authenticated,
        bool enabled = true,
        Dictionary<string, string?>? settings = null)
    {
        var configValues = settings ?? new Dictionary<string, string?>();
        configValues.TryAdd("RateLimiting:Enabled", enabled ? "true" : "false");

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddStandardRateLimiting(configuration);
                    })
                    .Configure(app =>
                    {
                        // Đứng trước limiter, đúng như UseAuthentication ở service thật.
                        app.Use(async (context, next) =>
                        {
                            if (authenticated)
                            {
                                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                                    [new Claim("AccountId", "11111111-1111-1111-1111-111111111111")],
                                    authenticationType: "TestScheme"));
                            }

                            await next();
                        });

                        app.UseRouting();
                        app.UseStandardRateLimiter();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/api/ping", () => Results.Ok("pong"));
                            endpoints.MapGet("/health", () => Results.Ok("healthy"));
                        });
                    });
            })
            .StartAsync();

        return host;
    }
}
