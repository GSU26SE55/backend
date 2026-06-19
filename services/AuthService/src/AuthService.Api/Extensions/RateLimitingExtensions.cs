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
    /// <summary>Rate limit cho /api/auth/login: 10 req/phút theo IP — chặn credential stuffing/brute force trước DB lockout.</summary>
    public const string PolicyLogin = "Login";
    /// <summary>Rate limit cho /api/auth/login/verify-2fa: 5 req/5min theo challengeToken (form/body). Fallback IP.</summary>
    public const string PolicyTwoFactorVerify = "TwoFactorVerify";
    /// <summary>Rate limit cho /api/accounts/me/2fa/disable: 3 req/5min theo UserId.</summary>
    public const string PolicyTwoFactorDisable = "TwoFactorDisable";
    /// <summary>Rate limit cho /api/accounts/me/2fa/backup-codes/regenerate: 3 req/giờ theo UserId.</summary>
    public const string PolicyBackupCodeRegenerate = "BackupCodeRegenerate";

    public static IServiceCollection AddOtpRateLimiting(this IServiceCollection services, TimeSpan? window = null)
    {
        var w = window ?? TimeSpan.FromMinutes(1);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyAnonOtp, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = w,
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
                        Window = w,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // #AUTH-04: login endpoint rate limit — 10/phút theo IP. Cao hơn AnonOtp (5/phút) vì
            // user typo password là common → tránh false-block; vẫn chặn được credential stuffing.
            options.AddPolicy(PolicyLogin, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "login:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            // 2FA Hardening (GH-295) — 3 policy thêm
            options.AddPolicy(PolicyTwoFactorVerify, ctx =>
            {
                // Partition theo challengeToken (header X-Challenge-Token hoặc query) — fallback IP
                var token = ctx.Request.Headers["X-Challenge-Token"].ToString();
                if (string.IsNullOrWhiteSpace(token))
                    token = ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "vfy:" + token,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.AddPolicy(PolicyTwoFactorDisable, ctx =>
            {
                var key = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? ctx.Connection.RemoteIpAddress?.ToString()
                          ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "dis:" + key,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.AddPolicy(PolicyBackupCodeRegenerate, ctx =>
            {
                var key = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? ctx.Connection.RemoteIpAddress?.ToString()
                          ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "regen:" + key,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
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
