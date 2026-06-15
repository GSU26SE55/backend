namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint 5B B9 (#154) — phân biệt nguồn đo của <c>SensorReading</c>.
///
/// Dùng để:
/// - Phục vụ cross-source validation (B10 §1.6.6) so sánh BMS vs IoT cùng asset.
/// - Audit traceability — biết reading đến từ BMS hay IoT edge device.
/// </summary>
public enum SensorReadingSourceTypeEnum
{
    /// <summary>Từ BMS gắn trực tiếp trong pack (qua RS485/Modbus).</summary>
    Bms = 1,

    /// <summary>Từ IoT edge device (ESP32-S3 + sensor ngoài INA226/DS18B20). Tên giữ legacy "Gateway".</summary>
    IotGateway = 2,

    /// <summary>Manual import, third-party feed.</summary>
    External = 3
}
