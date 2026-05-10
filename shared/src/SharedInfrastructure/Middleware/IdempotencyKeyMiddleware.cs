using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Idempotency;
using SharedInfrastructure.Metrics;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// Middleware Idempotency-Key. Áp dụng cho POST/PUT/PATCH có header "Idempotency-Key":
/// 1. Đã có cached response (status=done) → trả về luôn, không gọi controller.
/// 2. Reserve mới → cho qua controller, capture response, save vào cache.
/// 3. Reserve fail (đang xử lý parallel) → 409 Conflict.
/// Request không có header hoặc method GET/DELETE/OPTIONS/HEAD → bỏ qua, gọi next() bình thường.
/// </summary>
public class IdempotencyKeyMiddleware
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH"
    };

    private readonly RequestDelegate _next;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyKeyMiddleware> _logger;

    public IdempotencyKeyMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyKeyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyKeyStore store)
    {
        if (!_options.Enabled || !MutatingMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValue) || string.IsNullOrWhiteSpace(headerValue))
        {
            await _next(context);
            return;
        }

        var key = headerValue.ToString();
        var ttl = TimeSpan.FromHours(_options.TtlHours);

        // 1. Check cached response.
        var cached = await store.TryGetResponseAsync(key, context.RequestAborted);
        if (cached != null)
        {
            _logger.LogInformation("Idempotency replay for key {Key}.", key);
            AppMetrics.IdempotencyReplayHits.Inc();
            await WriteCachedResponseAsync(context, cached);
            return;
        }

        // 2. Reserve. Reserve fail = đang xử lý parallel = 409.
        var reserved = await store.TryReserveAsync(key, ttl, context.RequestAborted);
        if (!reserved)
        {
            // Có thể vừa được save xong giữa 2 lần đọc; thử lấy response cached lại 1 lần.
            var nowCached = await store.TryGetResponseAsync(key, context.RequestAborted);
            if (nowCached != null)
            {
                AppMetrics.IdempotencyReplayHits.Inc();
                await WriteCachedResponseAsync(context, nowCached);
                return;
            }

            AppMetrics.IdempotencyConflicts.Inc();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"isSuccess\":false,\"message\":\"Request đang được xử lý với cùng Idempotency-Key.\"}", context.RequestAborted);
            return;
        }

        AppMetrics.IdempotencyReservations.Inc();

        // 3. Capture response stream.
        var originalBody = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;

        try
        {
            await _next(context);

            captured.Position = 0;
            var bodyText = await new StreamReader(captured, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();

            // Copy về stream gốc để client nhận response.
            captured.Position = 0;
            await captured.CopyToAsync(originalBody, context.RequestAborted);

            await store.SaveResponseAsync(key, context.Response.StatusCode, bodyText, ttl, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task WriteCachedResponseAsync(HttpContext context, CachedIdempotencyResponse cached)
    {
        context.Response.StatusCode = cached.StatusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(cached.Body, context.RequestAborted);
    }
}
