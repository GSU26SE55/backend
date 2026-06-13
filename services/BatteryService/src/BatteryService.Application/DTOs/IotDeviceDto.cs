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
}

public class IotFirmwareCheckDto
{
    public bool UpdateAvailable { get; set; }
    public string? TargetVersion { get; set; }
    public string? ArtifactUrl { get; set; }
    public string? Sha256Checksum { get; set; }
    public long? ArtifactSizeBytes { get; set; }
    public string? UpdateLogId { get; set; }
    public string? ReleaseNotes { get; set; }
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
}
