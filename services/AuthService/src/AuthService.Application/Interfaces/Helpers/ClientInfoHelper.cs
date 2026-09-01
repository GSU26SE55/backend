using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using SharedInfrastructure.RateLimiting;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// Static utility resolve metadata client (IP, UserAgent, DeviceId) từ HttpContext.
/// Áp dụng cho mọi handler issue / refresh token (Login, VerifyOtp, RefreshToken, GoogleAuth).
///
/// Quy tắc:
/// - IP: ưu tiên header <see cref="RateLimitPartitionResolver.ClientIpHeader"/> ("X-Client-Ip") do
///   ApiGateway ghi đè (Set, không nối thêm) từ <c>Connection.RemoteIpAddress</c> quan sát được tại
///   gateway — cùng cơ chế anti-spoof mà <see cref="RateLimitPartitionResolver.ResolveClientIp"/>
///   đã dùng cho rate limiting. Fallback Connection.RemoteIpAddress (gọi thẳng, không qua gateway —
///   vd test/dev). #27 QA solars.io.vn 2026-08-29: trước đây đọc "X-Forwarded-For" — header này
///   KHÔNG được ApiGateway ghi/ghi đè nên luôn rỗng sau gateway, mọi request rơi xuống
///   Connection.RemoteIpAddress = IP pod của gateway (vd "10.42.0.8") thay vì IP trình duyệt thật.
///   Normalize IPv6 loopback (::1) → 127.0.0.1, IPv4-mapped IPv6 (::ffff:a.b.c.d) → a.b.c.d.
/// - UserAgent: header User-Agent.
/// - DeviceId: header X-Device-Id. Nếu client KHÔNG gửi → fallback SHA256(UA) prefix 'ua-'
///   để có giá trị deterministic theo browser, KHÔNG bao giờ trả null.
/// </summary>
public static class ClientInfoHelper
{
    public static (string? ip, string? userAgent, string? deviceId) Resolve(HttpContext? ctx)
    {
        if (ctx == null)
            return (null, null, null);

        var ip = NormalizeIp(ResolveRawIp(ctx));
        var ua = NullIfBlank(ctx.Request.Headers.UserAgent.ToString());
        var deviceId = NullIfBlank(ctx.Request.Headers["X-Device-Id"].FirstOrDefault())
                       ?? FallbackDeviceIdFromUserAgent(ua);

        return (ip, ua, deviceId);
    }

    private static string? ResolveRawIp(HttpContext ctx)
    {
        var fromGateway = ctx.Request.Headers[RateLimitPartitionResolver.ClientIpHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromGateway))
            return fromGateway.Trim();

        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Convert ::1 → 127.0.0.1 và ::ffff:a.b.c.d → a.b.c.d cho dễ đọc trong DB / log.
    /// </summary>
    private static string? NormalizeIp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!IPAddress.TryParse(raw, out var addr))
            return raw;

        if (IPAddress.IsLoopback(addr))
            return "127.0.0.1";
        if (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv4MappedToIPv6)
            return addr.MapToIPv4().ToString();
        return addr.ToString();
    }

    /// <summary>
    /// Khi client (Swagger / cURL / browser cơ bản) không gửi X-Device-Id,
    /// trả về hash UA để có id deterministic theo browser/user-agent. Format: "ua-{16-hex}".
    /// </summary>
    private static string? FallbackDeviceIdFromUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua))
            return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ua));
        var hex = Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        return $"ua-{hex}";
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
