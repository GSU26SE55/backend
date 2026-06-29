using BatteryService.Application.DTOs.Realtime;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint BE-IoT-Realtime (#615/#616) — publish reading ĐÃ làm sạch lên kênh realtime (Redis pub/sub).
/// Gọi SAU <c>SaveChangesAsync</c> trong ingest handler, là <b>soft-dependency</b> (try/catch, không chặn ingest).
/// </summary>
public interface ITelemetryPublisher
{
    /// <summary>Fan-out readings tới các kênh <c>telemetry:asset|customer|site:{id}</c>. No-op nếu Realtime:Enabled=false.</summary>
    Task PublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default);
}
