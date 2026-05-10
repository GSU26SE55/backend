using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

public class RequestLoggingMiddlewareTests
{
    private static IHostBuilder BuildHost(RequestDelegate downstream, RecordingLoggerProvider loggerProvider) =>
        new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddLogging(b =>
                    {
                        b.SetMinimumLevel(LogLevel.Information);
                        b.AddProvider(loggerProvider);
                    });
                })
                .Configure(app =>
                {
                    app.UseMiddleware<RequestLoggingMiddleware>();
                    app.Run(downstream);
                });
        });

    [Fact]
    public async Task Successful_Request_LogsAtInformationLevel()
    {
        var provider = new RecordingLoggerProvider();
        using var host = await BuildHost(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        }, provider).StartAsync();

        await host.GetTestClient().GetAsync("/api/something");

        provider.Logs.Should().ContainSingle(l =>
            l.Level == LogLevel.Information &&
            l.Message.Contains("HTTP") &&
            l.Message.Contains("/api/something") &&
            l.Message.Contains("200"));
    }

    [Fact]
    public async Task FourXX_Request_LogsAtWarning()
    {
        var provider = new RecordingLoggerProvider();
        using var host = await BuildHost(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, provider).StartAsync();

        await host.GetTestClient().GetAsync("/missing");

        provider.Logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("404"));
    }

    [Fact]
    public async Task FiveXX_Or_Exception_LogsAtError()
    {
        var provider = new RecordingLoggerProvider();
        using var host = await BuildHost(_ => throw new InvalidOperationException("boom"), provider).StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () => await host.GetTestClient().GetAsync("/explode"));

        provider.Logs.Should().Contain(l => l.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    [InlineData("/metrics")]
    [InlineData("/favicon.ico")]
    public async Task Skipped_Paths_AreNotLogged(string path)
    {
        var provider = new RecordingLoggerProvider();
        using var host = await BuildHost(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }, provider).StartAsync();

        await host.GetTestClient().GetAsync(path);

        provider.Logs.Should().NotContain(l => l.Message.Contains("HTTP"));
    }

    [Fact]
    public async Task Log_Contains_DurationAndMethod()
    {
        var provider = new RecordingLoggerProvider();
        using var host = await BuildHost(async ctx =>
        {
            await Task.Delay(10);
            ctx.Response.StatusCode = 201;
        }, provider).StartAsync();

        await host.GetTestClient().PostAsync("/users", new StringContent(""));

        var log = provider.Logs.Single(l => l.Message.Contains("HTTP"));
        log.Message.Should().Contain("POST");
        log.Message.Should().Contain("/users");
        log.Message.Should().Contain("ms");
        log.Message.Should().Contain("201");
    }

    /// <summary>
    /// Chỉ ghi log từ category RequestLoggingMiddleware, bỏ qua log của ASP.NET internals
    /// (Hosting/Routing/etc cũng có "HTTP" trong message, sẽ làm noise).
    /// </summary>
    private class RecordingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() { }

        private class Recorder : ILogger
        {
            private readonly RecordingLoggerProvider _parent;
            private readonly bool _capture;
            public Recorder(RecordingLoggerProvider p, string category)
            {
                _parent = p;
                _capture = category.Contains(nameof(RequestLoggingMiddleware));
            }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> formatter)
            {
                if (_capture)
                    _parent.Logs.Add((level, formatter(state, ex)));
            }
        }
    }
}
