namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint IoT-1 (#243) — scope cấp cho API key per-device.
/// Dùng bitmask để 1 key có thể có nhiều scope.
/// </summary>
[Flags]
public enum IotApiKeyScopeEnum
{
    None = 0,

    /// <summary>Cho phép POST /api/sensor-readings/batch (telemetry).</summary>
    SensorIngest = 1 << 0,

    /// <summary>Cho phép POST /api/iot-devices/heartbeat.</summary>
    DeviceHeartbeat = 1 << 1,

    /// <summary>Cho phép POST /api/ambient/ingest + environmental incident report.</summary>
    EnvironmentalIngest = 1 << 2,

    /// <summary>Cho phép GET /api/iot-devices/firmware-check + log update.</summary>
    FirmwareCheck = 1 << 3,

    /// <summary>Default bundle cho ESP32 edge device.</summary>
    /// <remarks>
    /// GH-785 — PHẢI gồm <see cref="EnvironmentalIngest"/>. Firmware xuất xưởng đã mang sẵn SHT31
    /// (nhiệt/ẩm môi trường), MQ2 (khí gas/khói) và cảm biến rò nước, nhưng bundle cũ
    /// (<c>SensorIngest | DeviceHeartbeat | FirmwareCheck</c> = 11) không cho gửi dữ liệu môi
    /// trường ⇒ thiết bị tạo theo mặc định bị chặn khi báo khói/gas/rò nước.
    /// <para>
    /// Đây không phải bất tiện nhỏ: đó là đường báo cháy và rò nước. Thiết bị chạy bình thường,
    /// telemetry vào đều, nên không ai nghi ngờ gì cho tới lúc cần cảnh báo an toàn thì nó im.
    /// </para>
    /// </remarks>
    EdgeDeviceDefault = SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck
}
