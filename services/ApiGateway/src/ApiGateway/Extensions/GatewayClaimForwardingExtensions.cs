using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway.Extensions;

/// <summary>
/// Sprint 7 #115 — forward claim của JWT đã validate xuống upstream service qua request header.
///
/// Anti-spoof: luôn xoá các header identity client tự gửi TRƯỚC khi set lại từ claim đã validate,
/// để client không thể giả mạo <c>X-User-*</c>.
/// </summary>
public static class GatewayClaimForwardingExtensions
{
    /// <summary>Các header identity gateway quản lý (xoá incoming + set từ claim).</summary>
    public static readonly string[] ForwardedHeaders =
        { "X-User-Id", "X-User-Role", "X-User-Email", "X-User-FullName" };

    public static IReverseProxyBuilder AddGatewayClaimForwarding(this IReverseProxyBuilder builder)
    {
        return builder.AddTransforms(context =>
        {
            context.AddRequestTransform(transformContext =>
            {
                // Anti-spoof — bỏ mọi header identity đã được copy từ request gốc.
                foreach (var header in ForwardedHeaders)
                {
                    transformContext.ProxyRequest.Headers.Remove(header);
                }

                var user = transformContext.HttpContext.User;
                if (user.Identity?.IsAuthenticated == true)
                {
                    Set(transformContext, "X-User-Id",
                        user.FindFirst("UserId")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                    Set(transformContext, "X-User-Role",
                        user.FindFirst("Role")?.Value ?? user.FindFirst(ClaimTypes.Role)?.Value);
                    Set(transformContext, "X-User-Email",
                        user.FindFirst("Email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value);
                    Set(transformContext, "X-User-FullName", user.FindFirst("FullName")?.Value);
                }

                return ValueTask.CompletedTask;
            });
        });
    }

    private static void Set(RequestTransformContext context, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            context.ProxyRequest.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
