using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-1 (#242, #244) — bản firmware ESP32 admin upload để OTA.
/// </summary>
public class IotFirmwareRelease : AuditableEntity
{
    /// <summary>SemVer "1.2.3".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Hardware revision tương thích ("v1.0-S3-MAX485"). Match với <see cref="IotDevice.HardwareRevision"/>.</summary>
    public string HardwareRevision { get; set; } = string.Empty;

    /// <summary>URL artifact (FileStorageService presigned). Device GET trực tiếp.</summary>
    public string ArtifactUrl { get; set; } = string.Empty;

    /// <summary>SHA-256 của artifact để device verify sau khi download.</summary>
    public string Sha256Checksum { get; set; } = string.Empty;

    /// <summary>Kích thước artifact (bytes).</summary>
    public long ArtifactSizeBytes { get; set; }

    /// <summary>Changelog/release notes markdown.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Đánh dấu bản này là rollout chính thức.</summary>
    public bool IsPublished { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>Bản này đã bị admin rollback / disable.</summary>
    public bool IsArchived { get; set; }

    public ICollection<IotFirmwareUpdateLog> UpdateLogs { get; set; } = new List<IotFirmwareUpdateLog>();
}
