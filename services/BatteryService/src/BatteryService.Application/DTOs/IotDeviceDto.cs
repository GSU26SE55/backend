using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class IotDeviceDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Mã device duy nhất (vd ESP32-001).</summary>
    public string DeviceCode { get; set; } = string.Empty;
    /// <summary>Tên gợi nhớ hiển thị UI.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Tên của site.</summary>
    public string? SiteName { get; set; }
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Filter theo status enum.</summary>
    public IotDeviceStatusEnum Status { get; set; }
    /// <summary>Firmware version device đang chạy (báo qua heartbeat).</summary>
    public string? CurrentFirmwareVersion { get; set; }
    /// <summary>ID firmware release đang đặt làm target OTA (nullable).</summary>
    public string? TargetFirmwareReleaseId { get; set; }
    /// <summary>Firmware version target cho OTA.</summary>
    public string? TargetFirmwareVersion { get; set; }
    /// <summary>Bitmask scopes API key (SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck).</summary>
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; }
    /// <summary>4 ký tự cuối của API key — hiển thị UI nhận diện.</summary>
    public string ApiKeyLastFour { get; set; } = string.Empty;
    /// <summary>Timestamp cấp API key.</summary>
    public DateTime ApiKeyIssuedAt { get; set; }
    /// <summary>Timestamp revoke API key (null nếu còn hiệu lực).</summary>
    public DateTime? ApiKeyRevokedAt { get; set; }
    /// <summary>Heartbeat gần nhất (UTC).</summary>
    public DateTime? LastSeenAt { get; set; }
    /// <summary>Provision gần nhất.</summary>
    public DateTime? LastProvisionedAt { get; set; }
    /// <summary>Lần cuối chuyển sang Offline.</summary>
    public DateTime? LastOfflineAt { get; set; }
    /// <summary>Tần suất heartbeat (giây, default 60).</summary>
    public int HeartbeatIntervalSeconds { get; set; }
    /// <summary>Sai lệch đồng hồ device vs server (giây).</summary>
    public double? LastClockSkewSeconds { get; set; }
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

public class IotDeviceCreatedDto : IotDeviceDto
{
    /// <summary>Plaintext API key. Trả 1 lần duy nhất khi create/rotate.</summary>
    public string RawApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Sprint IoT-2 #IoT2-07 — provisioning payload encoded để Admin print QR code dán vào device.
    /// Format: <c>iot://provision?dc={deviceCode}&amp;key={rawApiKey}</c>.
    /// ESP32 quét bằng camera nội bộ hoặc Admin paste vào NVS partition khi flash.
    /// </summary>
    public string ProvisioningQrCode { get; set; } = string.Empty;

    /// <summary>Sprint IoT-2 #IoT2-26 — MQTT username (= deviceCode lowercase). Set vào ESP32 firmware config.</summary>
    public string? MqttUsername { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-26 — plaintext MQTT password. Trả 1 lần duy nhất khi create/rotate.</summary>
    public string? MqttPassword { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-26 — broker hostname (Mosquitto/EMQX).</summary>
    public string? MqttBrokerHost { get; set; }

    /// <summary>MQTT broker port (1883 plain / 8883 TLS).</summary>
    public int? MqttBrokerPort { get; set; }
}

public class IotFirmwareReleaseDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Phiên bản theo SemVer X.Y.Z.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string HardwareRevision { get; set; } = string.Empty;
    /// <summary>URL absolute của artifact .bin (FileStorage presigned).</summary>
    public string ArtifactUrl { get; set; } = string.Empty;
    /// <summary>SHA-256 hex (64 chars) của artifact để device verify.</summary>
    public string Sha256Checksum { get; set; } = string.Empty;
    /// <summary>Kích thước artifact (bytes, &le; 50MB).</summary>
    public long ArtifactSizeBytes { get; set; }
    /// <summary>Changelog/release notes (markdown).</summary>
    public string? ReleaseNotes { get; set; }
    /// <summary>Đã publish (cho phép đặt làm target OTA).</summary>
    public bool IsPublished { get; set; }
    /// <summary>Timestamp publish.</summary>
    public DateTime? PublishedAt { get; set; }
    /// <summary>Đã archive/rollback.</summary>
    public bool IsArchived { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    // Sprint IoT-2 #IoT2-35.
    /// <summary>Force update — device bắt buộc apply.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public IotFirmwareChannelEnum Channel { get; set; } = IotFirmwareChannelEnum.Stable;
    /// <summary>Model device (vd "ESP32-S3-WROOM-1").</summary>
    public string? DeviceModel { get; set; }
}

public class IotFirmwareCheckDto
{
    /// <summary>Field UpdateAvailable.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — alias rõ nghĩa spec dùng <c>hasUpdate</c>.</summary>
    public bool HasUpdate
    {
        get => UpdateAvailable;
        set => UpdateAvailable = value;
    }

    /// <summary>Field TargetVersion.</summary>
    public string? TargetVersion { get; set; }
    /// <summary>URL absolute của artifact .bin (FileStorage presigned).</summary>
    public string? ArtifactUrl { get; set; }

    /// <summary>Spec alias — signed URL device download artifact.</summary>
    public string? DownloadUrl
    {
        get => ArtifactUrl;
        set => ArtifactUrl = value;
    }

    /// <summary>SHA-256 hex (64 chars) của artifact để device verify.</summary>
    public string? Sha256Checksum { get; set; }
    /// <summary>Kích thước artifact (bytes, &le; 50MB).</summary>
    public long? ArtifactSizeBytes { get; set; }
    /// <summary>ID update log để device gọi PUT cập nhật progress.</summary>
    public string? UpdateLogId { get; set; }
    /// <summary>Changelog/release notes (markdown).</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — force update flag từ release.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — channel của release (Stable/Beta).</summary>
    public IotFirmwareChannelEnum Channel { get; set; } = IotFirmwareChannelEnum.Stable;
}

public class IotHeartbeatAckDto
{
    /// <summary>Field ServerTime.</summary>
    public DateTime ServerTime { get; set; }
    /// <summary>Skew đồng hồ heartbeat hiện tại (giây).</summary>
    public double ClockSkewSeconds { get; set; }
    /// <summary>True nếu clock skew vượt ngưỡng 5 phút.</summary>
    public bool ClockSkewWarning { get; set; }
    /// <summary>Tần suất heartbeat backend recommend cho lần kế tiếp.</summary>
    public int NextHeartbeatInSeconds { get; set; }
    /// <summary>True nếu target firmware khác current — gọi /firmware-check để lấy artifact.</summary>
    public bool FirmwareUpdateAvailable { get; set; }
}

public class IotDeviceProvisionResultDto
{
    /// <summary>ID IoT device (Guid).</summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>Mã device duy nhất (vd ESP32-001).</summary>
    public string DeviceCode { get; set; } = string.Empty;
    /// <summary>ID Site (Guid).</summary>
    public string SiteId { get; set; } = string.Empty;
    /// <summary>Tần suất heartbeat (giây, default 60).</summary>
    public int HeartbeatIntervalSeconds { get; set; }
    /// <summary>Bitmask scopes API key (SensorIngest | DeviceHeartbeat | EnvironmentalIngest | FirmwareCheck).</summary>
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; }
    /// <summary>Firmware version target cho OTA.</summary>
    public string? TargetFirmwareVersion { get; set; }

    // Sprint IoT-2 #IoT2-09 (S2-BE-06) — configJson fields chuẩn theo §52.3/§52.4.
    /// <summary>Tần suất poll sensor (giây). Device dùng để pace ADC sampling.</summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>NTP server cho device đồng bộ clock (chống skew). Default Google pool.</summary>
    public string NtpServer { get; set; } = "time.google.com";

    /// <summary>Mapping battery serial → unitId Modbus + sensorSourceCode để route data.</summary>
    public List<BatteryMappingEntry> BatteryMappings { get; set; } = new();

    /// <summary>Danh sách sensor type device được phép push: ["voltage","current","temperature","soc","sensor-ambient",...]</summary>
    public List<string> SupportedSensors { get; set; } = new();
}

/// <summary>Sprint IoT-2 #IoT2-09 — Modbus address + sensor channel cho 1 battery.</summary>
public class BatteryMappingEntry
{
    /// <summary>Serial của BatteryAsset — backend resolve thành ID nếu cần.</summary>
    public string BatteryAssetSerial { get; set; } = string.Empty;
    /// <summary>ID của Unit (Guid).</summary>
    public int? UnitId { get; set; }
    /// <summary>Code phân biệt sensor cùng pin ("primary" / "redundant").</summary>
    public string? SensorSourceCode { get; set; }
}
