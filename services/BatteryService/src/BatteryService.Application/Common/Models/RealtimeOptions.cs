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

    /// <summary>
    /// Sprint BE-IoT-Realtime <c>#614</c> — số reading gần nhất giữ lại MỖI PIN để phát lại khi
    /// client reconnect kèm <c>Last-Event-ID</c>. Dùng <c>MAXLEN ~</c> nên Redis cắt xấp xỉ (rẻ hơn
    /// cắt chính xác). 200 ≈ vài phút dữ liệu ở nhịp ingest thường — đủ cứu các lần rớt mạng vài
    /// chục giây của app mobile, mà tốn rất ít RAM.
    /// </summary>
    public int ReplayMaxEvents { get; set; } = 200;

    /// <summary>
    /// Hạn sống của stream replay (phút). Refresh mỗi lần ghi. Pin ngừng gửi số liệu thì key tự dọn,
    /// không để rác tồn đọng trong Redis. 0 = không đặt TTL.
    /// </summary>
    public int ReplayTtlMinutes { get; set; } = 5;
}
