using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    private static IHostBuilder BuildHost(RequestDelegate downstream) =>
        new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(services => services.AddLogging())
                .Configure(app =>
                {
                    app.UseMiddleware<GlobalExceptionMiddleware>();
                    app.Run(downstream);
                });
        });

    [Fact]
    public async Task Invoke_NoException_PassesThroughDownstream()
    {
        using var host = await BuildHost(async ctx =>
        {
            ctx.Response.StatusCode = 204;
            await ctx.Response.WriteAsync(string.Empty);
        }).StartAsync();

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Invoke_DownstreamThrows_Returns500_WithStandardJsonShape()
    {
        using var host = await BuildHost(_ => throw new InvalidOperationException("boom"))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        // #AUTH-76: Vietnamese localized message + hint correlationId cho support correlate.
        doc.RootElement.GetProperty("message").GetString()
            .Should().Be("Đã xảy ra lỗi hệ thống. Vui lòng liên hệ support kèm correlationId.");
        // #AUTH-76: data payload có correlationId thay vì null (dev env có thêm exceptionType/exceptionMessage).
        // Test env default (không IsProduction) → dev shape; chỉ assert correlationId tồn tại.
        var dataElement = doc.RootElement.GetProperty("data");
        dataElement.ValueKind.Should().Be(JsonValueKind.Object);
        dataElement.TryGetProperty("correlationId", out _).Should().BeTrue();
        doc.RootElement.GetProperty("listErrors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invoke_DownstreamThrows_LogsError()
    {
        var loggerFactory = new TrackingLoggerFactory();

        using var host = await new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ILoggerFactory>(loggerFactory);
                })
                .Configure(app =>
                {
                    app.UseMiddleware<GlobalExceptionMiddleware>();
                    app.Run(_ => throw new Exception("kaboom"));
                });
        }).StartAsync();

        await host.GetTestClient().GetAsync("/");

        loggerFactory.Logger.LoggedErrors.Should().NotBeEmpty();
        loggerFactory.Logger.LoggedErrors[0].exception?.Message.Should().Be("kaboom");
    }

    private class TrackingLoggerFactory : ILoggerFactory
    {
        public TrackingLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private class TrackingLogger : ILogger
    {
        public List<(LogLevel level, Exception? exception, string message)> LoggedErrors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                LoggedErrors.Add((logLevel, exception, formatter(state, exception)));
        }
    }
}
