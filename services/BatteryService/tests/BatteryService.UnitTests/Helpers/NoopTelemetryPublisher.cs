using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// Sprint BE-IoT-Realtime — no-op <see cref="ITelemetryPublisher"/> cho test ingest handler
/// (realtime publish là soft-dependency, không ảnh hưởng kết quả ingest).
/// </summary>
public sealed class NoopTelemetryPublisher : ITelemetryPublisher
{
    public Task PublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
