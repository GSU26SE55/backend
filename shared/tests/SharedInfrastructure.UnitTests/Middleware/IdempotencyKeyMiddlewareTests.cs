using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

public class IdempotencyKeyMiddlewareTests
{
    private static IHostBuilder BuildHost(IIdempotencyKeyStore store, RequestDelegate downstream,
        IdempotencyOptions? options = null) =>
        new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddLogging();
                    s.AddSingleton(store);
                    s.AddSingleton<IOptions<IdempotencyOptions>>(
                        Options.Create(options ?? new IdempotencyOptions { Enabled = true, TtlHours = 24 }));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<IdempotencyKeyMiddleware>();
                    app.Run(downstream);
                });
        });

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    public async Task NonMutatingMethod_BypassesMiddleware(string method)
    {
        var store = new Mock<IIdempotencyKeyStore>(MockBehavior.Strict);  // Strict: any unconfigured call → fail
        var hit = false;

        using var host = await BuildHost(store.Object, ctx =>
        {
            hit = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }).StartAsync();

        var req = new HttpRequestMessage(new HttpMethod(method), "/");
        req.Headers.Add(IdempotencyKeyMiddleware.HeaderName, "should-be-ignored");
        await host.GetTestClient().SendAsync(req);

        hit.Should().BeTrue();
        store.VerifyNoOtherCalls();  // store không được gọi
    }

    [Fact]
    public async Task PostWithoutHeader_BypassesMiddleware()
    {
        var store = new Mock<IIdempotencyKeyStore>(MockBehavior.Strict);
        var hit = false;

        using var host = await BuildHost(store.Object, ctx =>
        {
            hit = true;
            ctx.Response.StatusCode = 201;
            return Task.CompletedTask;
        }).StartAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/");
        var resp = await host.GetTestClient().SendAsync(req);

        hit.Should().BeTrue();
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CachedResponse_ReplaysWithoutCallingDownstream()
    {
        var store = new Mock<IIdempotencyKeyStore>();
        store.Setup(s => s.TryGetResponseAsync("key1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(new CachedIdempotencyResponse(201, "{\"cached\":true}"));

        var downstreamHit = false;
        using var host = await BuildHost(store.Object, ctx =>
        {
            downstreamHit = true;
            return Task.CompletedTask;
        }).StartAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/");
        req.Headers.Add(IdempotencyKeyMiddleware.HeaderName, "key1");
        var resp = await host.GetTestClient().SendAsync(req);

        downstreamHit.Should().BeFalse();
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Be("{\"cached\":true}");
    }

    [Fact]
    public async Task ReserveFails_NoCachedResponse_Returns409()
    {
        var store = new Mock<IIdempotencyKeyStore>();
        store.Setup(s => s.TryGetResponseAsync("key2", It.IsAny<CancellationToken>())).ReturnsAsync((CachedIdempotencyResponse?)null);
        store.Setup(s => s.TryReserveAsync("key2", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        using var host = await BuildHost(store.Object, ctx => Task.CompletedTask).StartAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/");
        req.Headers.Add(IdempotencyKeyMiddleware.HeaderName, "key2");
        var resp = await host.GetTestClient().SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task FreshKey_RunsDownstream_AndCachesResponse()
    {
        var store = new Mock<IIdempotencyKeyStore>();
        store.Setup(s => s.TryGetResponseAsync("key3", It.IsAny<CancellationToken>())).ReturnsAsync((CachedIdempotencyResponse?)null);
        store.Setup(s => s.TryReserveAsync("key3", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var host = await BuildHost(store.Object, async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"hello\":\"world\"}");
        }).StartAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/");
        req.Headers.Add(IdempotencyKeyMiddleware.HeaderName, "key3");
        var resp = await host.GetTestClient().SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Be("{\"hello\":\"world\"}");

        store.Verify(s => s.SaveResponseAsync("key3", 200, "{\"hello\":\"world\"}", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyHeaderValue_TreatsAsMissing()
    {
        var store = new Mock<IIdempotencyKeyStore>(MockBehavior.Strict);

        using var host = await BuildHost(store.Object, ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }).StartAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/");
        req.Headers.Add(IdempotencyKeyMiddleware.HeaderName, " ");
        var resp = await host.GetTestClient().SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        store.VerifyNoOtherCalls();
    }
}
