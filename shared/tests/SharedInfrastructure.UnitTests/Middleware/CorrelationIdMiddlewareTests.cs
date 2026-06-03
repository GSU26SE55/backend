using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

public class CorrelationIdMiddlewareTests
{
    private static IHostBuilder BuildHost(RequestDelegate downstream) =>
        new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(s => s.AddLogging())
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(downstream);
                });
        });

    [Fact]
    public async Task NoHeader_GeneratesNewCorrelationId_AndSetsResponseHeader()
    {
        string? capturedId = null;
        using var host = await BuildHost(ctx =>
        {
            capturedId = ctx.GetCorrelationId();
            return Task.CompletedTask;
        }).StartAsync();

        var response = await host.GetTestClient().GetAsync("/");

        capturedId.Should().NotBeNullOrEmpty();
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().Contain(capturedId!);
    }

    [Fact]
    public async Task ClientProvidedHeader_IsForwarded_NotOverwritten()
    {
        const string clientId = "client-trace-abc-123";
        string? captured = null;

        using var host = await BuildHost(ctx =>
        {
            captured = ctx.GetCorrelationId();
            return Task.CompletedTask;
        }).StartAsync();

        var client = host.GetTestClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add(CorrelationIdMiddleware.HeaderName, clientId);
        var response = await client.SendAsync(req);

        captured.Should().Be(clientId);
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Should().Contain(clientId);
    }

    [Fact]
    public Task GetCorrelationId_OnNullContext_ReturnsNull()
    {
        HttpContext? nullCtx = null;
        nullCtx.GetCorrelationId().Should().BeNull();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GeneratedId_Is32CharHexUuid()
    {
        string? id = null;
        using var host = await BuildHost(ctx =>
        {
            id = ctx.GetCorrelationId();
            return Task.CompletedTask;
        }).StartAsync();

        await host.GetTestClient().GetAsync("/");

        id.Should().NotBeNull();
        id!.Length.Should().Be(32);
        id.Should().MatchRegex("^[0-9a-f]{32}$");
    }
}
