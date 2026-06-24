using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SharedInfrastructure.Middleware;

namespace ApiGateway.Extensions;

/// <summary>
/// Sprint 7 #115 — rate limiting tại gateway. Fixed-window partition theo userId (nếu đã auth)
/// hoặc IP. Vượt giới hạn → 429 với CommonResponse chuẩn.
/// </summary>
public static class GatewayRateLimitingExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 100);
        var windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey =
                    httpContext.User.FindFirst("UserId")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0
                });
            });

            options.OnRejected = async (context, _) =>
            {
                await CommonResponseWriter.WriteAsync(
                    context.HttpContext.Response,
                    StatusCodes.Status429TooManyRequests,
                    "Quá nhiều yêu cầu. Vui lòng thử lại sau.",
                    errors: null,
                    data: new { errorCode = "RATE_LIMITED" });
            };
        });

        return services;
    }
}
