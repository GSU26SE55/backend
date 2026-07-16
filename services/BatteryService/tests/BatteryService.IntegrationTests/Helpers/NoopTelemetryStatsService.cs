using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// Sprint Bonus NS-03/NS-04 — no-op <see cref="ITelemetryStatsService"/> cho integration test ingest
/// handler (stats streaming là soft-dependency). Bản sao của helper bên UnitTests vì project
/// IntegrationTests không reference UnitTests — giữ cùng namespace cho nhất quán cách dùng.
/// </summary>
public sealed class NoopTelemetryStatsService : ITelemetryStatsService
{
    public Task AccumulateAndPublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
