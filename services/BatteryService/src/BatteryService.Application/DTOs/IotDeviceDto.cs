using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class IotDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string? SiteName { get; set; }
    public string? HardwareRevision { get; set; }
    public IotDeviceStatusEnum Status { get; set; }
    public string? CurrentFirmwareVersion { get; set; }
    public string? TargetFirmwareReleaseId { get; set; }
    public string? TargetFirmwareVersion { get; set; }
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; }
    public string ApiKeyLastFour { get; set; } = string.Empty;
    public DateTime ApiKeyIssuedAt { get; set; }
    public DateTime? ApiKeyRevokedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastProvisionedAt { get; set; }
    public DateTime? LastOfflineAt { get; set; }
    public int HeartbeatIntervalSeconds { get; set; }
    public double? LastClockSkewSeconds { get; set; }
    public string? Notes { get; set; }
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

    public int? MqttBrokerPort { get; set; }
}

public class IotFirmwareReleaseDto
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string HardwareRevision { get; set; } = string.Empty;
    public string ArtifactUrl { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long ArtifactSizeBytes { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }

    // Sprint IoT-2 #IoT2-35.
    public bool IsRequired { get; set; }
    public IotFirmwareChannelEnum Channel { get; set; } = IotFirmwareChannelEnum.Stable;
    public string? DeviceModel { get; set; }
}

public class IotFirmwareCheckDto
{
    public bool UpdateAvailable { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — alias rõ nghĩa spec dùng <c>hasUpdate</c>.</summary>
    public bool HasUpdate
    {
        get => UpdateAvailable;
        set => UpdateAvailable = value;
    }

    public string? TargetVersion { get; set; }
    public string? ArtifactUrl { get; set; }

    /// <summary>Spec alias — signed URL device download artifact.</summary>
    public string? DownloadUrl
    {
        get => ArtifactUrl;
        set => ArtifactUrl = value;
    }

    public string? Sha256Checksum { get; set; }
    public long? ArtifactSizeBytes { get; set; }
    public string? UpdateLogId { get; set; }
    public string? ReleaseNotes { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — force update flag từ release.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Sprint IoT-2 #IoT2-36 — channel của release (Stable/Beta).</summary>
    public IotFirmwareChannelEnum Channel { get; set; } = IotFirmwareChannelEnum.Stable;
}

public class IotHeartbeatAckDto
{
    public DateTime ServerTime { get; set; }
    public double ClockSkewSeconds { get; set; }
    public bool ClockSkewWarning { get; set; }
    public int NextHeartbeatInSeconds { get; set; }
    public bool FirmwareUpdateAvailable { get; set; }
}

public class IotDeviceProvisionResultDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public int HeartbeatIntervalSeconds { get; set; }
    public IotApiKeyScopeEnum ApiKeyScopes { get; set; }
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
    public string BatteryAssetSerial { get; set; } = string.Empty;
    public int? UnitId { get; set; }
    public string? SensorSourceCode { get; set; }
}
