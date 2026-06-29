using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace TicketService.Api.Extensions;

/// <summary>
/// Built-in .NET 8 rate limiter cho chat write endpoint (Add/Edit/Delete/Pin/Unpin) — theo đúng pattern
/// đã có ở <c>AuthService.Api.Extensions.RateLimitingExtensions</c> (#520).
/// Limit: Customer 10/phút/ticket, Staff 30/phút/global, Manager 60/phút/global, Admin unlimited.
/// </summary>
public static class ChatRateLimitingExtensions
{
    public const string ChatWritePolicy = "ChatWrite";

    public static IServiceCollection AddChatRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(ChatWritePolicy, httpContext =>
            {
                var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value
                           ?? httpContext.User.FindFirst("role")?.Value;
                var userId = httpContext.User.FindFirst("AccountId")?.Value
                             ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? "anon";

                if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter("admin:" + userId);

                if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    var ticketId = httpContext.Request.RouteValues["ticketId"]?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"customer:{ticketId}:{userId}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });
                }

                if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"manager:{userId}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });
                }

                // Staff (default fallback)
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"staff:{userId}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        return services;
    }
}
