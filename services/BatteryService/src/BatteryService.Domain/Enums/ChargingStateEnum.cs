namespace BatteryService.Domain.Enums;

/// <summary>
/// Trạng thái nạp/xả của pin tại thời điểm đo. Được report từ BMS.
/// </summary>
public enum ChargingStateEnum
{
    Idle = 1,           // Không nạp/xả (current ~ 0)
    Charging = 2,       // Đang nạp (current > 0 theo convention BMS)
    Discharging = 3,    // Đang xả (current < 0)
    Float = 4,          // Float charge (giữ điện áp full)
    Bypass = 5          // Bypass mode (đi tắt qua battery, dùng nguồn lưới trực tiếp)
}
