namespace BatteryService.Domain.Enums;

/// <summary>
/// Sprint IoT-1 (#242) — trạng thái OTA firmware update của 1 device.
/// </summary>
public enum IotFirmwareUpdateStatusEnum
{
    /// <summary>Đã ra lệnh, device chưa báo kết quả.</summary>
    Pending = 1,

    /// <summary>Device đang download artifact.</summary>
    Downloading = 2,

    /// <summary>Device đang flash.</summary>
    Installing = 3,

    /// <summary>Update thành công, đã reboot.</summary>
    Success = 4,

    /// <summary>Update fail (CRC/checksum/rollback).</summary>
    Failed = 5,

    /// <summary>Device không đủ điều kiện (battery thấp, không tải) → bỏ qua round này.</summary>
    Skipped = 6
}
