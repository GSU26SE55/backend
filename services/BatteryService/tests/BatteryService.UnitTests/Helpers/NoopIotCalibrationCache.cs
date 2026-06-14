using BatteryService.Application.Services;

namespace BatteryService.UnitTests.Helpers;

/// <summary>Always-miss cache — tests luôn fallback DB.</summary>
public sealed class NoopIotCalibrationCache : IIotCalibrationCache
{
    public Task<List<IotDeviceCalibrationSnapshot>?> GetAsync(Guid iotDeviceId, CancellationToken ct = default)
        => Task.FromResult<List<IotDeviceCalibrationSnapshot>?>(null);

    public Task SetAsync(Guid iotDeviceId, List<IotDeviceCalibrationSnapshot> snapshot, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task InvalidateAsync(Guid iotDeviceId, CancellationToken ct = default) => Task.CompletedTask;
}
