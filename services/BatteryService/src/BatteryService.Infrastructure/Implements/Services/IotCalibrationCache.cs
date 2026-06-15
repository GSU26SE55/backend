using BatteryService.Application.Services;
using SharedContracts.Interfaces;

namespace BatteryService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint IoT-2 #IoT2-19 / #IoT2-34 — Get/Set/Invalidate calibration snapshot per device qua Redis.
/// Key format: <c>iot:calibration:{deviceId:N}</c>. TTL 5 phút.
/// </summary>
public class IotCalibrationCache : IIotCalibrationCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly ICacheService _cache;

    public IotCalibrationCache(ICacheService cache) => _cache = cache;

    public Task<List<IotDeviceCalibrationSnapshot>?> GetAsync(Guid iotDeviceId, CancellationToken ct = default)
        => _cache.GetAsync<List<IotDeviceCalibrationSnapshot>>(KeyOf(iotDeviceId), ct);

    public Task SetAsync(Guid iotDeviceId, List<IotDeviceCalibrationSnapshot> snapshot, CancellationToken ct = default)
        => _cache.SetAsync(KeyOf(iotDeviceId), snapshot, Ttl, ct);

    public Task InvalidateAsync(Guid iotDeviceId, CancellationToken ct = default)
        => _cache.RemoveAsync(KeyOf(iotDeviceId), ct);

    private static string KeyOf(Guid id) => $"iot:calibration:{id:N}";
}
