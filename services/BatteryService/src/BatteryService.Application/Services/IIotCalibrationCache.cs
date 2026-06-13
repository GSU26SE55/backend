using BatteryService.Domain.Entities;

namespace BatteryService.Application.Services;

/// <summary>
/// Sprint IoT-2 #IoT2-19/#IoT2-34 — cache calibration profile của 1 device để handler ingest
/// không phải query DB mỗi batch. Redis TTL 5 phút.
/// Khi tạo/xoá calibration → <see cref="InvalidateAsync"/> để reading kế tiếp dùng giá trị mới.
/// </summary>
public interface IIotCalibrationCache
{
    /// <summary>Lấy snapshot calibration hiện hành của device — null nếu cache miss; caller fallback DB.</summary>
    Task<List<IotDeviceCalibrationSnapshot>?> GetAsync(Guid iotDeviceId, CancellationToken ct = default);

    /// <summary>Set snapshot (TTL 5 phút).</summary>
    Task SetAsync(Guid iotDeviceId, List<IotDeviceCalibrationSnapshot> snapshot, CancellationToken ct = default);

    Task InvalidateAsync(Guid iotDeviceId, CancellationToken ct = default);
}

/// <summary>
/// Snapshot tuần tự hóa được — không kéo full entity (tránh circular ref).
/// </summary>
public class IotDeviceCalibrationSnapshot
{
    public string Channel { get; set; } = string.Empty;
    public Guid? BatteryAssetId { get; set; }
    public decimal Scale { get; set; }
    public decimal Offset { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public static IotDeviceCalibrationSnapshot FromEntity(IotDeviceCalibration c) => new()
    {
        Channel = c.Channel,
        BatteryAssetId = c.BatteryAssetId,
        Scale = c.Scale,
        Offset = c.Offset,
        ExpiresAt = c.ExpiresAt
    };
}
