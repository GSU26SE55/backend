using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Middleware;

/// <summary>
/// GH-800 — client tự đóng luồng SSE bị ghi nhận thành 502.
/// </summary>
/// <remarks>
/// <para>
/// Luồng telemetry cảm biến sống lâu và client đóng lại là chuyện bình thường; YARP đánh trạng thái
/// downstream thành 502 nên cả log lẫn <c>http_requests_received_total</c> đều ghi một lỗi 5xx dù dữ
/// liệu đã truyền xong. Tỉ lệ lỗi giả làm SLO sai và che mất những cú 502 THẬT.
/// </para>
/// <para>
/// Hai chiều đều phải đúng thì bản sửa mới có giá trị: client huỷ ⇒ không tính 5xx; upstream chết
/// thật ⇒ vẫn phải là 502. Chỉ kiểm chiều đầu thì cách "sửa" đơn giản nhất là bỏ đếm 5xx đi.
/// </para>
/// </remarks>
public class ClientDisconnectStatusTests
{
    // ── Quyết định thuần (không cần dựng pipeline) ────────────────────────────

    [Fact]
    public void AbortedRequestWith502_IsRewrittenToClientClosed()
    {
        ClientDisconnectStatusMiddleware.ShouldRewrite(
            clientAborted: true, statusCode: 502, responseHasStarted: false).Should().BeTrue();
    }

    [Fact]
    public void GenuineUpstreamFailure_IsLeftAlone()
    {
        // Upstream chết thì client vẫn đang chờ — không có huỷ nào cả. Đây là điều kiện giữ lại
        // những cú 502 thật; thiếu nó thì bản sửa chỉ là tắt cảnh báo đi.
        ClientDisconnectStatusMiddleware.ShouldRewrite(
            clientAborted: false, statusCode: 502, responseHasStarted: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(404)]
    public void AbortedRequestWithNon5xx_KeepsItsStatus(int statusCode)
    {
        // Client huỷ giữa chừng một phản hồi 200 thì 200 vẫn mô tả đúng: dữ liệu đã truyền thật.
        ClientDisconnectStatusMiddleware.ShouldRewrite(
            clientAborted: true, statusCode: statusCode, responseHasStarted: false).Should().BeFalse();
    }

    [Fact]
    public void OnceTheResponseHasStarted_TheStatusIsNotTouched()
    {
        // Byte đã ra khỏi máy chủ với mã trạng thái cũ; ASP.NET cũng ném lỗi nếu cố ghi đè.
        ClientDisconnectStatusMiddleware.ShouldRewrite(
            clientAborted: true, statusCode: 502, responseHasStarted: true).Should().BeFalse();
    }

    // ── Trên pipeline HTTP thật ───────────────────────────────────────────────

    private static async Task<IHost> StartHostAsync(RequestDelegate terminal)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .Configure(app =>
                {
                    app.UseMiddleware<ClientDisconnectStatusMiddleware>();
                    app.Run(terminal);
                }))
            .StartAsync();
        return host;
    }

    [Fact]
    public async Task RealPipeline_AbortedRequest_EndsAsClientClosed()
    {
        // Mô phỏng đúng cảnh YARP gặp: client đã ngắt, và trạng thái downstream là 502.
        using var host = await StartHostAsync(ctx =>
        {
            ctx.Response.StatusCode = 502;
            return Task.CompletedTask;
        });

        var context = await host.GetTestServer().SendAsync(ctx =>
        {
            ctx.Request.Path = "/api/sensor-readings/stream";
            ctx.RequestAborted = new CancellationToken(canceled: true);
        });

        context.Response.StatusCode.Should().Be(ClientDisconnectStatusMiddleware.ClientClosedRequest);
    }

    [Fact]
    public async Task RealPipeline_UpstreamDown_StillReports502()
    {
        using var host = await StartHostAsync(ctx =>
        {
            ctx.Response.StatusCode = 502;
            return Task.CompletedTask;
        });

        var context = await host.GetTestServer().SendAsync(ctx =>
        {
            ctx.Request.Path = "/api/sensor-readings/stream";
            // Không huỷ: client vẫn đang chờ câu trả lời.
        });

        context.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task RealPipeline_SuccessfulRequest_IsUntouched()
    {
        using var host = await StartHostAsync(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = await host.GetTestServer().SendAsync(ctx => ctx.Request.Path = "/api/health");

        context.Response.StatusCode.Should().Be(200);
    }

    // ── Mức log của RequestLoggingMiddleware ─────────────────────────────────

    [Fact]
    public async Task AbortedRequest_IsNotLoggedAsAnError()
    {
        // Cảnh báo lỗi giả cũng tốn đúng thời gian của người trực như cảnh báo thật, và lặp lại đủ
        // nhiều thì người ta bắt đầu bỏ qua cả những cái thật.
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/sensor-readings/stream";
        context.Response.StatusCode = 502;
        context.RequestAborted = new CancellationToken(canceled: true);

        var sut = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await sut.InvokeAsync(context);

        logger.Levels.Should().NotContain(LogLevel.Error);
        logger.Levels.Should().Contain(LogLevel.Information);
    }

    [Fact]
    public async Task GenuineServerError_IsStillLoggedAsAnError()
    {
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/batteries";
        context.Response.StatusCode = 500;

        var sut = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await sut.InvokeAsync(context);

        logger.Levels.Should().Contain(LogLevel.Error);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
