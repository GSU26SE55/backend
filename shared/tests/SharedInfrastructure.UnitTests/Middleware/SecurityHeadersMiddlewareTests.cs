using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static IHostBuilder BuildHost(RequestDelegate downstream) =>
        new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(s => s.AddLogging())
                .Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.Run(downstream);
                });
        });

    [Fact]
    public async Task Response_Has_X_Content_Type_Options_Nosniff()
    {
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task Response_Has_X_Frame_Options_DENY()
    {
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    [Fact]
    public async Task Response_Has_ReferrerPolicy()
    {
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task Response_Has_PermissionsPolicy_DisablesCameraMicGeo()
    {
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        var permissionsPolicy = response.Headers.GetValues("Permissions-Policy").Single();
        permissionsPolicy.Should().Contain("camera=()");
        permissionsPolicy.Should().Contain("microphone=()");
        permissionsPolicy.Should().Contain("geolocation=()");
    }

    [Fact]
    public async Task Response_Has_X_Permitted_Cross_Domain_Policies_None()
    {
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        response.Headers.GetValues("X-Permitted-Cross-Domain-Policies").Should().Contain("none");
    }

    [Fact]
    public async Task NonHttpsRequest_DoesNotSetHsts()
    {
        // TestServer mặc định IsHttps=false → HSTS không nên set
        using var host = await BuildHost(ctx => ctx.Response.WriteAsync("ok")).StartAsync();
        var response = await host.GetTestClient().GetAsync("/");
        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }

    [Fact]
    public async Task DownstreamThrows_PropagatesException()
    {
        // Middleware không nuốt exception — phải propagate lên TestServer
        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer()
                .ConfigureServices(s => s.AddLogging())
                .Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.Run(_ => throw new InvalidOperationException("boom"));
                });
        });

        using var host = await hostBuilder.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.GetTestClient().GetAsync("/"));
    }
}
