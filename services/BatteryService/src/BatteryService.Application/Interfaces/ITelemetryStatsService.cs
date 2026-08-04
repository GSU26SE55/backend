using BatteryService.Application.DTOs.Realtime;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint Bonus NS-03/NS-04 (#648/#649) — tích lũy rolling min/max dòng nạp/xả (state Redis theo
/// window) rồi publish event SSE <c>stats</c>. Gọi SAU <c>SaveChangesAsync</c> trong ingest handler,
/// là <b>soft-dependency</b> y hệt <see cref="ITelemetryPublisher"/> (try/catch, lỗi Redis KHÔNG chặn
/// ingest). No-op khi <c>Realtime:Enabled=false</c>.
/// </summary>
public interface ITelemetryStatsService
{
    /// <summary>
    /// Lọc reading primary, tách chiều nạp/xả, merge min/max vào state Redis (window 1h + today)
    /// và fan-out event <c>stats</c> lên kênh <c>telemetry:stats:*</c>.
    /// </summary>
    Task AccumulateAndPublishAsync(
        IReadOnlyList<LiveReadingDto> readings,
        CancellationToken cancellationToken = default);
}
