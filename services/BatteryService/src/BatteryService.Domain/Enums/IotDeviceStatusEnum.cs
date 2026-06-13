namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint IoT-1 (#242) — vòng đời của một IoT edge device (ESP32-S3).
/// </summary>
public enum IotDeviceStatusEnum
{
    /// <summary>Đã tạo nhưng chưa provision (chưa có firmware/credential bind).</summary>
    Pending = 1,

    /// <summary>Đã provision và heartbeat trong threshold (5 phút).</summary>
    Active = 2,

    /// <summary>Mất heartbeat &gt; 5 phút — đánh dấu bởi OfflineDetection job.</summary>
    Offline = 3,

    /// <summary>Bị admin vô hiệu hoá (API key revoked, không nhận ingest).</summary>
    Disabled = 4,

    /// <summary>Đã ngưng sử dụng — không tham gia ingest/heartbeat nữa.</summary>
    Decommissioned = 5
}
