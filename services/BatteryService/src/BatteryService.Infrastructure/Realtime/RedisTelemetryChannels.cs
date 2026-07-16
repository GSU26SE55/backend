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

    // ── Sprint Bonus NS-04 (#649) — kênh riêng cho event `stats` ──
    // Prefix riêng "telemetry:stats:*". TUYỆT ĐỐI KHÔNG dùng chung channel `reading` cũ:
    // RedisTelemetryStream.Handler deserialize MỌI message trên channel reading thành LiveReadingDto
    // → nhét stats vào sẽ vỡ parser/coalescer summary (§4.5.1 newsprint).
    public const string StatsPrefix = Prefix + ":stats";
    public const string StatsAll = StatsPrefix + ":all";
    public const string StatsSiteNone = StatsPrefix + ":site:none";

    public static string StatsAsset(Guid id) => $"{StatsPrefix}:asset:{id:N}";
    public static string StatsCustomer(Guid id) => $"{StatsPrefix}:customer:{id:N}";
    public static string StatsSite(Guid id) => $"{StatsPrefix}:site:{id:N}";
    public static string StatsType(Guid id) => $"{StatsPrefix}:type:{id:N}";

    /// <summary>Các kênh stats cần subscribe cho 1 scope (song song với <see cref="ChannelsFor"/>).</summary>
    public static IReadOnlyList<string> StatsChannelsFor(TelemetryScope scope) => scope.Kind switch
    {
        TelemetryScopeType.Asset => scope.Ids.Select(StatsAsset).ToList(),
        TelemetryScopeType.Customer => scope.Ids.Select(StatsCustomer).ToList(),
        TelemetryScopeType.Site => scope.Ids.Select(StatsSite).ToList(),
        TelemetryScopeType.BatteryType => scope.Ids.Select(StatsType).ToList(),
        TelemetryScopeType.All => new[] { StatsAll },
        TelemetryScopeType.SiteNone => new[] { StatsSiteNone },
        _ => Array.Empty<string>()
    };
}
