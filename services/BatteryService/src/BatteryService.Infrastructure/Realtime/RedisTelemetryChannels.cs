using BatteryService.Application.Realtime;

namespace BatteryService.Infrastructure.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime (#615) — quy ước tên Redis pub/sub channel (§34.10.2).
/// Publisher fan-out mỗi reading lên: asset · customer · site (hoặc site:none) · type · all.
/// Stream subscribe theo scope (1 hoặc NHIỀU channel). Tách riêng để publisher & stream không lệch tên.
/// </summary>
public static class RedisTelemetryChannels
{
    public const string Prefix = "telemetry";
    public const string All = Prefix + ":all";
    public const string SiteNone = Prefix + ":site:none";

    public static string Asset(Guid id) => $"{Prefix}:asset:{id:N}";
    public static string Customer(Guid id) => $"{Prefix}:customer:{id:N}";
    public static string Site(Guid id) => $"{Prefix}:site:{id:N}";
    public static string Type(Guid id) => $"{Prefix}:type:{id:N}";

    /// <summary>Các channel cần subscribe cho 1 scope (multi-asset/site → nhiều channel).</summary>
    public static IReadOnlyList<string> ChannelsFor(TelemetryScope scope) => scope.Kind switch
    {
        TelemetryScopeType.Asset => scope.Ids.Select(Asset).ToList(),
        TelemetryScopeType.Customer => scope.Ids.Select(Customer).ToList(),
        TelemetryScopeType.Site => scope.Ids.Select(Site).ToList(),
        TelemetryScopeType.BatteryType => scope.Ids.Select(Type).ToList(),
        TelemetryScopeType.All => new[] { All },
        TelemetryScopeType.SiteNone => new[] { SiteNone },
        _ => Array.Empty<string>()
    };
}
