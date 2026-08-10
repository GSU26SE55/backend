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
    /// <summary>
    /// GH-776 — rate limit cho /api/auth/introspect: 60 req/phút theo IP.
    /// </summary>
    /// <remarks>
    /// Khoá truy cập đã chặn người lạ; giới hạn này là lớp thứ hai cho ca khoá bị lộ, và chặn luôn
    /// việc dò khoá bằng vét cạn. 60/phút thoải mái cho một resource server thật (nó cache kết quả
    /// theo vòng đời token, không hỏi lại mỗi request) mà vẫn quá thấp để dùng làm oracle.
    /// </remarks>
    public const string PolicyIntrospect = "Introspect";

    public static IServiceCollection AddOtpRateLimiting(this IServiceCollection services, TimeSpan? window = null)
    {
        var w = window ?? TimeSpan.FromMinutes(1);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(PolicyIntrospect, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

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

            // OnRejected + RejectionStatusCode CỐ TÌNH không đặt ở đây.
            // `AddRateLimiter` dùng options pattern nên mọi lần gọi đều ghi vào cùng một object:
            // hai nơi cùng đặt OnRejected thì nơi đăng ký sau ghi đè nơi trước, và hình dạng response
            // 429 sẽ đổi theo thứ tự đăng ký. Nơi duy nhất giữ trách nhiệm này là
            // SharedInfrastructure.RateLimiting.StandardRateLimitingExtensions.
        });

        return services;
    }
}
