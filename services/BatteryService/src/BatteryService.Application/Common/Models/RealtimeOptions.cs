namespace BatteryService.Application.Common.Models;

/// <summary>
/// Sprint BE-IoT-Realtime (#614..#623) — cấu hình kênh SSE telemetry.
/// Xem overall.md §34.10. Tắt <see cref="Enabled"/> → publisher no-op, ingest vẫn chạy bình thường.
/// </summary>
public class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Bật/tắt toàn bộ kênh realtime (soft-dependency, mặc định true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Heartbeat <c>ping</c> giữ kết nối SSE sống (giây) — §34.10.9.</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>Throttle gom <c>summary</c> cho scope customer/site (giây) — §34.10.5.</summary>
    public int SummaryIntervalSeconds { get; set; } = 4;
}
