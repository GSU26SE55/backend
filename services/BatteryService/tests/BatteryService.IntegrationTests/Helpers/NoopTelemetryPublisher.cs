using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// Sprint BE-IoT-Realtime — no-op <see cref="ITelemetryPublisher"/> cho integration test ingest handler
/// (realtime publish là soft-dependency, không ảnh hưởng kết quả ingest). Bản sao của helper bên UnitTests
/// vì project IntegrationTests không reference UnitTests — giữ cùng namespace cho nhất quán cách dùng.
/// </summary>
public sealed class NoopTelemetryPublisher : ITelemetryPublisher
{
    public Task PublishAsync(IReadOnlyList<LiveReadingDto> readings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
