using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharedInfrastructure.RateLimiting;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Extensions;

/// <summary>
/// Chuyển IP thật của client xuống upstream service qua header
/// <see cref="RateLimitPartitionResolver.ClientIpHeader"/>.
/// </summary>
/// <remarks>
/// Sau gateway, <c>RemoteIpAddress</c> mà service nhìn thấy luôn là IP container của gateway. Thiếu
/// header này thì hạn mức ẩn danh của từng service gom TOÀN BỘ traffic chưa đăng nhập vào một bộ đếm
/// duy nhất — một người dùng bình thường có thể làm cả hệ thống bị chặn.
///
/// Anti-spoof: xoá header client tự gửi rồi mới đặt lại từ <c>RemoteIpAddress</c> mà gateway quan sát
/// được — cùng cách <see cref="GatewayClaimForwardingExtensions"/> xử lý header identity.
/// </remarks>
public static class GatewayClientIpForwardingExtensions
{
    /// <summary>
    /// Xoá <see cref="RateLimitPartitionResolver.ClientIpHeader"/> mà client gửi tới, NGAY ĐẦU pipeline.
    /// </summary>
    /// <remarks>
    /// Bắt buộc, và phải đứng trước <c>UseStandardRateLimiter()</c>. Gateway là biên ngoài cùng: nó tự
    /// đọc header này để chọn bộ đếm, nên nếu để nguyên giá trị client gắn vào thì kẻ tấn công chỉ cần
    /// đổi header mỗi request là mở được vô số bộ đếm và hạn mức 60 req/30s mất tác dụng hoàn toàn.
    /// Service phía sau thì ngược lại — chúng TIN header này, vì lúc đó giá trị đã do gateway ghi
    /// (giả định: service chỉ tiếp nhận traffic qua gateway, không expose thẳng ra ngoài).
    /// </remarks>
    public static IApplicationBuilder UseClientIpHeaderSanitizer(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Request.Headers.Remove(RateLimitPartitionResolver.ClientIpHeader);
            await next();
        });
    }

    public static IReverseProxyBuilder AddGatewayClientIpForwarding(this IReverseProxyBuilder builder)
    {
        return builder.AddTransforms(context =>
        {
            context.AddRequestTransform(transformContext =>
            {
                transformContext.ProxyRequest.Headers.Remove(RateLimitPartitionResolver.ClientIpHeader);

                var clientIp = transformContext.HttpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrWhiteSpace(clientIp))
                {
                    transformContext.ProxyRequest.Headers.TryAddWithoutValidation(
                        RateLimitPartitionResolver.ClientIpHeader,
                        clientIp);
                }

                return ValueTask.CompletedTask;
            });
        });
    }
}
