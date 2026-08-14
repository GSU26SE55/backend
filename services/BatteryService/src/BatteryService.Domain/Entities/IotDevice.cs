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

    /// <summary>Hash SHA-256 của API key. Dùng để verify constant-time khi device gọi API.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Plaintext API key đầy đủ (prefix <c>iotk_</c>) — lưu để Admin xem lại trên
    /// <c>GET /api/admin/iot-devices/{id}</c>. <c>null</c> cho device tạo trước khi bật lưu plaintext
    /// (rotate-key để populate). Set/replace mỗi lần create + rotate.
    /// </summary>
    public string? ApiKeyPlaintext { get; set; }

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

    /// <summary>
    /// IOT3-77 — chu kỳ đọc cảm biến (giây) mà thiết bị dùng để đặt nhịp poll BMS.
    /// </summary>
    /// <remarks>
    /// Trước IOT3-77 giá trị này là số cứng <c>10</c> trong <c>ProvisionIotDeviceCommandHandler</c>,
    /// nên Admin không đổi được qua web — chỉ đổi tạm bằng lệnh MQTT <c>set_interval</c> (ghi RAM,
    /// mất sau reboot). Biên hợp lệ <b>[1, 600]</b> đặt để KHỚP clamp phía firmware
    /// (<c>provision.cpp</c>): biên rộng hơn thì firmware âm thầm clamp lại và Admin tưởng đã đổi.
    /// </remarks>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>Sai lệch đồng hồ device vs server gần nhất (giây). Dùng để cảnh báo clock drift.</summary>
    public double? LastClockSkewSeconds { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-17 — số outlier reading tích lũy trong cửa sổ 1h gần nhất. &gt;50 → auto-Decommissioned.</summary>
    public int OutlierIncidentCount { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-17 — mốc bắt đầu cửa sổ đếm outlier hiện tại. Vượt 1h kể từ lúc này → reset counter.</summary>
    public DateTime? OutlierWindowStartedAt { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-17 — lần cuối hệ thống auto-decommission do outlier vượt ngưỡng.</summary>
    public DateTime? AutoDecommissionedAt { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-26 — MQTT username cấp cho device (sync EMQX/Mosquitto ACL).</summary>
    public string? MqttUsername { get; set; }

    /// <summary>
    /// Sprint IoT-2 #IoT2-26 — hash PBKDF2-SHA512 định dạng <c>$7$</c> của Mosquitto.
    /// <see cref="MqttPasswordFileSyncService"/> chép giá trị này thẳng vào file <c>passwd</c>.
    /// </summary>
    public string? MqttPasswordHash { get; set; }

    /// <summary>
    /// IOT3-25 — plaintext mật khẩu MQTT, để <c>/provision</c> cấp lại cho thiết bị mỗi lần boot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MqttPasswordHash"/> là PBKDF2 một chiều — không đọc ngược được. Không lưu
    /// plaintext thì thiết bị chỉ nhận được mật khẩu ĐÚNG MỘT LẦN lúc admin tạo device, tức là
    /// vẫn phải nhúng cứng vào firmware — đúng cái Phương án A muốn xoá bỏ.
    /// </para>
    /// <para>
    /// Cùng khuôn <see cref="ApiKeyPlaintext"/> (chốt 16/07/2026): cùng bảng, cùng endpoint admin,
    /// cùng lớp quyền — không mở ra loại phơi nhiễm mới. <c>null</c> với device tạo trước IOT3-25;
    /// <c>ProvisionIotDeviceCommandHandler</c> tự sinh lại khi gặp (xem IOT3-28).
    /// </para>
    /// </remarks>
    public string? MqttPasswordPlaintext { get; set; }

    /// <summary>Mô tả vị trí, ghi chú lắp đặt.</summary>
    public string? Notes { get; set; }

    public ICollection<IotDeviceHeartbeat> Heartbeats { get; set; } = new List<IotDeviceHeartbeat>();

    public ICollection<IotDeviceCalibration> Calibrations { get; set; } = new List<IotDeviceCalibration>();

    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();

    public ICollection<IotFirmwareUpdateLog> FirmwareUpdateLogs { get; set; } = new List<IotFirmwareUpdateLog>();

    public ICollection<IotDeviceCommand> Commands { get; set; } = new List<IotDeviceCommand>();
}
