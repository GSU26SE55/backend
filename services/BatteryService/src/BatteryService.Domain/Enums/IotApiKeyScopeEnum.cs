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
    EdgeDeviceDefault = SensorIngest | DeviceHeartbeat | FirmwareCheck
}
