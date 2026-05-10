using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Sinh hoặc forward Correlation ID qua mọi request. Lưu vào HttpContext.Items["CorrelationId"]
/// và push vào log scope để mọi log line trong cùng request có cùng ID.
/// Header: X-Correlation-ID. Nếu client gửi → tái sử dụng. Nếu không → sinh UUID mới.
/// Response cũng include header này để client/upstream trace được.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await _next(context);
        }
    }
}

public static class CorrelationIdAccessor
{
    /// <summary>
    /// Lấy Correlation ID từ HttpContext. Trả về null nếu chưa qua middleware.
    /// </summary>
    public static string? GetCorrelationId(this HttpContext? context)
    {
        return context?.Items[CorrelationIdMiddleware.ItemKey] as string;
    }
}
