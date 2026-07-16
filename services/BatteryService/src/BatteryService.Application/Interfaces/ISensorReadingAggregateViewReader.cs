using BatteryService.Application.DTOs;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint Bonus NS-06 (#650, PA-4) — đọc continuous aggregate <c>sensor_readings_agg_1h</c>
/// (TimescaleDB materialized view — không map entity nên đọc bằng raw SQL trong Infrastructure).
/// Trả cùng shape <see cref="SensorReadingAggregateDto"/> với endpoint /aggregate (interval 1h).
/// </summary>
public interface ISensorReadingAggregateViewReader
{
    Task<IReadOnlyList<SensorReadingAggregateDto>> ReadHourlyAsync(
        Guid batteryAssetId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
}
