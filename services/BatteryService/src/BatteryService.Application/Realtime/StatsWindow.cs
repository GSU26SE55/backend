namespace BatteryService.Application.Realtime;

/// <summary>Sprint Bonus NS-03 (#648) — window rolling min/max. CHỐT chỉ 2 window (Q3).</summary>
public enum StatsWindowType
{
    /// <summary>Giờ hiện tại (UTC), key kèm yyyyMMddHH, TTL 2h.</summary>
    Hour = 1,

    /// <summary>Từ 00:00 UTC hôm nay, key kèm yyyyMMdd, TTL 26h.</summary>
    Today = 2
}

/// <summary>Sprint Bonus NS-03 (#648) — quy ước label/key/windowStart/TTL cho từng window (UTC toàn hệ thống).</summary>
public static class StatsWindows
{
    public static readonly IReadOnlyList<StatsWindowType> All = new[] { StatsWindowType.Hour, StatsWindowType.Today };

    /// <summary>Label khớp field <c>window</c> của payload SSE (§5.3bis docs realtime).</summary>
    public static string Label(StatsWindowType window) => window switch
    {
        StatsWindowType.Hour => "1h",
        StatsWindowType.Today => "today",
        _ => window.ToString()
    };

    /// <summary>Mốc bắt đầu window chứa <paramref name="utc"/> (floor theo giờ / ngày).</summary>
    public static DateTime WindowStart(StatsWindowType window, DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return window switch
        {
            StatsWindowType.Hour => new DateTime(u.Year, u.Month, u.Day, u.Hour, 0, 0, DateTimeKind.Utc),
            StatsWindowType.Today => new DateTime(u.Year, u.Month, u.Day, 0, 0, 0, DateTimeKind.Utc),
            _ => u
        };
    }

    /// <summary>Suffix key Redis phân biệt bucket (yyyyMMddHH cho 1h, yyyyMMdd cho today).</summary>
    public static string KeySuffix(StatsWindowType window, DateTime utc)
    {
        var start = WindowStart(window, utc);
        return window switch
        {
            StatsWindowType.Hour => start.ToString("yyyyMMddHH"),
            StatsWindowType.Today => start.ToString("yyyyMMdd"),
            _ => start.ToString("yyyyMMddHH")
        };
    }

    /// <summary>TTL đủ dài để giữ bucket sống hết window + buffer (Redis tự dọn).</summary>
    public static TimeSpan Ttl(StatsWindowType window) => window switch
    {
        StatsWindowType.Hour => TimeSpan.FromHours(2),
        StatsWindowType.Today => TimeSpan.FromHours(26),
        _ => TimeSpan.FromHours(2)
    };
}
