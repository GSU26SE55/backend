using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Log mỗi HTTP request kèm: method, path, query, status, duration, userId, correlationId.
/// Log level theo status:
/// - 5xx: Error (kèm exception nếu có)
/// - 4xx: Warning
/// - khác: Information
/// Bỏ qua endpoint /health và /metrics để tránh spam log.
/// </summary>
public class RequestLoggingMiddleware
{
    private static readonly HashSet<string> SkipPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/health/ready", "/health/live", "/metrics", "/favicon.ico"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (SkipPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? exception = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            // GH-800 — client tự đóng kết nối không phải lỗi máy chủ.
            //
            // Luồng SSE sống lâu; client đóng lại là chuyện bình thường, nhưng YARP đánh trạng thái
            // downstream thành 502 và dòng log này ghi ra một lỗi 5xx dù dữ liệu đã truyền xong.
            // Tỉ lệ lỗi giả làm SLO sai và che mất những cú 502 THẬT.
            //
            // Ở đây nhận diện theo RequestAborted chứ không sửa Response.StatusCode: khi phản hồi đã
            // bắt đầu (đúng trường hợp SSE) thì trạng thái không sửa được nữa —
            // ClientDisconnectStatusMiddleware lo phần sửa được, còn phần này lo phần còn lại.
            var clientAborted = context.RequestAborted.IsCancellationRequested;
            var status = clientAborted && context.Response.StatusCode >= 500
                ? ClientDisconnectStatusMiddleware.ClientClosedRequest
                : context.Response.StatusCode;

            var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var correlationId = context.GetCorrelationId();

            var level = clientAborted
                ? LogLevel.Information
                : status >= 500 || exception != null
                    ? LogLevel.Error
                    : status >= 400
                        ? LogLevel.Warning
                        : LogLevel.Information;

            _logger.Log(level, exception,
                "HTTP {Method} {Path}{Query} → {StatusCode} in {Elapsed}ms (user={UserId}, corrId={CorrelationId})",
                context.Request.Method,
                path,
                context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty,
                status,
                sw.ElapsedMilliseconds,
                userId ?? "anon",
                correlationId ?? "none");
        }
    }
}
