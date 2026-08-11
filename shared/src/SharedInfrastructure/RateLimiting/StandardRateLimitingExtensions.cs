using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedInfrastructure.Metrics;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.RateLimiting;

/// <summary>
/// Hạn mức request nền, dùng chung cho toàn bộ service kể cả ApiGateway.
/// Xem <see cref="StandardRateLimitOptions"/> cho ý nghĩa từng con số.
/// </summary>
public static class StandardRateLimitingExtensions
{
    /// <summary>
    /// Đăng ký limiter nền. Gọi được cùng với các <c>AddPolicy</c> riêng của service —
    /// <c>AddRateLimiter</c> dùng options pattern nên nhiều lần gọi sẽ cộng dồn cấu hình.
    /// </summary>
    /// <remarks>
    /// Đây là nơi DUY NHẤT đặt <c>OnRejected</c> và <c>RejectionStatusCode</c>. Các extension riêng của
    /// service chỉ được thêm policy; nếu chúng cũng đặt <c>OnRejected</c> thì lần gọi sau ghi đè lần gọi
    /// trước và hình dạng response 429 sẽ khác nhau tuỳ thứ tự đăng ký.
    /// </remarks>
    public static IServiceCollection AddStandardRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(StandardRateLimitOptions.SectionName);
        services.Configure<StandardRateLimitOptions>(section);

        var options = new StandardRateLimitOptions();
        section.Bind(options);

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var decision = RateLimitPartitionResolver.Resolve(httpContext, options);

                if (decision.IsExempt)
                    return RateLimitPartition.GetNoLimiter(decision.PartitionKey);

                return RateLimitPartition.GetFixedWindowLimiter(
                    decision.PartitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = decision.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    });
            });

            limiterOptions.OnRejected = async (context, _) =>
            {
                var httpContext = context.HttpContext;

                // Retry-After để client biết chờ bao lâu thay vì thử lại ngay và bị chặn tiếp.
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : options.WindowSeconds;

                if (!httpContext.Response.HasStarted)
                    httpContext.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

                var scope = httpContext.User?.Identity?.IsAuthenticated == true ? "authenticated" : "anonymous";
                AppMetrics.HttpRateLimitedTotal.WithLabels(scope).Inc();

                await CommonResponseWriter.WriteAsync(
                    httpContext.Response,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests. Please try again later.",
                    errors: null,
                    data: new { errorCode = "RATE_LIMITED", retryAfterSeconds });
            };
        });

        return services;
    }

    /// <summary>
    /// Bật limiter. PHẢI gọi SAU <c>UseAuthentication()</c> và <c>UseAuthorization()</c>.
    /// </summary>
    /// <remarks>
    /// Vị trí này là điều kiện để phân biệt hai bậc hạn mức, không phải chuyện thẩm mỹ:
    /// <list type="bullet">
    ///   <item><description><c>UseAuthentication</c> mới gán <c>HttpContext.User</c> cho JWT. Đặt limiter trước nó thì mọi request đều bị coi là chưa đăng nhập.</description></item>
    ///   <item><description><c>UseAuthorization</c> mới xác thực các scheme chỉ định riêng ở endpoint — thiết bị IoT dùng <c>[Authorize(AuthenticationSchemes = "ApiKey")]</c> chỉ có danh tính sau bước này. Đặt limiter trước nó thì toàn bộ thiết bị IoT rơi xuống bậc ẩn danh.</description></item>
    /// </list>
    /// </remarks>
    public static IApplicationBuilder UseStandardRateLimiter(this IApplicationBuilder app)
    {
        return app.UseRateLimiter();
    }
}
