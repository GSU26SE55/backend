using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// Sprint Bonus NS-03/NS-04 — no-op <see cref="ITelemetryStatsService"/> cho test ingest handler
/// (stats streaming là soft-dependency, không ảnh hưởng kết quả ingest).
/// </summary>
public sealed class NoopTelemetryStatsService : ITelemetryStatsService
{
    public Task AccumulateAndPublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
