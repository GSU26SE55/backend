using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AuthService.Api.Extensions;

/// <summary>
/// Built-in .NET 8 rate limiter. Cung cấp 2 policy chuẩn cho OTP-style endpoints:
/// - "AnonOtp": 5 req/phút theo IP. Dùng cho endpoint anonymous (Register, ForgotPassword, ResendOtp, ResendResetOtp).
/// - "AuthOtp": 3 req/phút theo UserId (claim NameIdentifier). Dùng cho endpoint cần JWT (Enable2FA, SendPhoneOtp).
/// Vượt limit → 429 Too Many Requests với JSON body.
/// </summary>
public static class RateLimitingExtensions
{
    public const string PolicyAnonOtp = "AnonOtp";
    public const string PolicyAuthOtp = "AuthOtp";

    public static IServiceCollection AddOtpRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyAnonOtp, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            options.AddPolicy(PolicyAuthOtp, ctx =>
            {
                var key = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? ctx.Connection.RemoteIpAddress?.ToString()
                          ?? "anon";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: key,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.OnRejected = async (ctx, token) =>
            {
                ctx.HttpContext.Response.StatusCode = 429;
                ctx.HttpContext.Response.ContentType = "application/json";
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
                }
                await ctx.HttpContext.Response.WriteAsync(
                    "{\"isSuccess\":false,\"statusCode\":429,\"message\":\"Quá nhiều yêu cầu. Vui lòng thử lại sau.\"}",
                    token);
            };
        });

        return services;
    }
}
