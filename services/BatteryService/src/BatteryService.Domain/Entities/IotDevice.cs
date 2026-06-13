using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-1 (#242) — ESP32 edge device đặt tại site, đẩy sensor batch + heartbeat lên backend.
/// 1 device có thể nối nhiều <see cref="BatteryAsset"/> qua RS485/Modbus multi-drop (xem §52.10).
/// </summary>
public class IotDevice : AuditableEntity
{
    /// <summary>Code duy nhất cấp cho device, in qua label/QR (vd "ESP32-001").</summary>
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>Tên gợi nhớ (vd "Site A - Rack 1 gateway").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Site mà device đang đặt. Bắt buộc — không cho phép device "orphan".</summary>
    public Guid SiteId { get; set; }

    public Site Site { get; set; } = null!;

    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }

    public IotDeviceStatusEnum Status { get; set; } = IotDeviceStatusEnum.Pending;

    /// <summary>Firmware version device báo lên trong heartbeat gần nhất (vd "1.2.3").</summary>
    public string? CurrentFirmwareVersion { get; set; }

    /// <summary>Firmware admin đặt làm target — OTA pipeline kiểm tra mismatch.</summary>
    public Guid? TargetFirmwareReleaseId { get; set; }

    public IotFirmwareRelease? TargetFirmwareRelease { get; set; }

    /// <summary>Hash SHA-256 của API key. Plaintext chỉ trả 1 lần khi rotate/issue.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>4 ký tự cuối của key — hiển thị trong UI để admin nhận diện ("…ab12").</summary>
    public string ApiKeyLastFour { get; set; } = string.Empty;

    /// <summary>Bitmask <see cref="IotApiKeyScopeEnum"/>.</summary>
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; } = IotApiKeyScopeEnum.EdgeDeviceDefault;

    public DateTime ApiKeyIssuedAt { get; set; }

    public DateTime? ApiKeyRevokedAt { get; set; }

    /// <summary>Heartbeat gần nhất nhận được. Null = chưa từng heartbeat.</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Lần cuối device báo provision thành công.</summary>
    public DateTime? LastProvisionedAt { get; set; }

    /// <summary>Lần cuối hệ thống chuyển device từ Active → Offline.</summary>
    public DateTime? LastOfflineAt { get; set; }

    /// <summary>Heartbeat interval device tự khai (giây). Default 60.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    /// <summary>Sai lệch đồng hồ device vs server gần nhất (giây). Dùng để cảnh báo clock drift.</summary>
    public double? LastClockSkewSeconds { get; set; }

    /// <summary>Mô tả vị trí, ghi chú lắp đặt.</summary>
    public string? Notes { get; set; }

    public ICollection<IotDeviceHeartbeat> Heartbeats { get; set; } = new List<IotDeviceHeartbeat>();

    public ICollection<IotDeviceCalibration> Calibrations { get; set; } = new List<IotDeviceCalibration>();

    public ICollection<IotFirmwareUpdateLog> FirmwareUpdateLogs { get; set; } = new List<IotFirmwareUpdateLog>();
}
