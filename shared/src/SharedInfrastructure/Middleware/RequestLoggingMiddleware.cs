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

            var status = context.Response.StatusCode;
            var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var correlationId = context.GetCorrelationId();

            var level = status >= 500 || exception != null
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
