using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #AUTH-48: Sinh device fingerprint hash + IP prefix cho TrustedDevice lookup.
///
/// Fingerprint = SHA-256(DeviceId + "|" + UserAgent) — KHÔNG lưu raw PII.
/// IP prefix = /24 IPv4 (vd "203.0.113.0/24") hoặc /64 IPv6 → tolerate DHCP cùng subnet,
/// reject từ mạng/quốc gia khác.
/// </summary>
public static class TrustedDeviceFingerprintHelper
{
    /// <summary>
    /// Trả về SHA-256 hex (64 chars). DeviceId hoặc UserAgent null → trả null (không trust được).
    /// </summary>
    public static string? ComputeFingerprint(string? deviceId, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(userAgent))
            return null;
        var canonical = deviceId.Trim() + "|" + userAgent.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Convert IP về prefix /24 (IPv4) hoặc /64 (IPv6). Null/invalid → null.
    /// </summary>
    public static string? ComputeIpPrefix(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var addr))
            return null;

        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes(); // 4 bytes
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Lấy 64 high bits đầu (8 bytes), format ::-shortened.
            var bytes = addr.GetAddressBytes(); // 16 bytes
            var groups = new ushort[4];
            for (var i = 0; i < 4; i++)
                groups[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
            return $"{groups[0]:x}:{groups[1]:x}:{groups[2]:x}:{groups[3]:x}::/64";
        }

        return null;
    }

    /// <summary>
    /// Sinh label friendly từ UserAgent (vd "Chrome on macOS"). Fallback "Unknown device".
    /// </summary>
    public static string GenerateLabel(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown device";

        var ua = userAgent.ToLowerInvariant();
        var browser = ua switch
        {
            _ when ua.Contains("edg/") => "Edge",
            _ when ua.Contains("chrome/") && !ua.Contains("edg/") => "Chrome",
            _ when ua.Contains("firefox/") => "Firefox",
            _ when ua.Contains("safari/") && !ua.Contains("chrome/") => "Safari",
            _ when ua.Contains("opera/") || ua.Contains("opr/") => "Opera",
            _ => "Browser"
        };
        var os = ua switch
        {
            _ when ua.Contains("iphone") => "iPhone",
            _ when ua.Contains("ipad") => "iPad",
            _ when ua.Contains("android") => "Android",
            _ when ua.Contains("mac os x") || ua.Contains("macintosh") => "macOS",
            _ when ua.Contains("windows") => "Windows",
            _ when ua.Contains("linux") => "Linux",
            _ => "Unknown OS"
        };
        return $"{browser} on {os}";
    }
}
