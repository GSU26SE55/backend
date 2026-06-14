namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-35 — response sau khi upload .bin qua multipart.
/// Admin dùng <c>ArtifactUrl</c> + <c>Sha256Checksum</c> + <c>ArtifactSizeBytes</c>
/// để gọi tiếp <c>POST /api/admin/iot-firmware-releases</c> tạo metadata release.
/// </summary>
public class FirmwareBinaryUploadDto
{
    /// <summary>URL absolute của artifact .bin (FileStorage presigned).</summary>
    public string ArtifactUrl { get; set; } = string.Empty;
    /// <summary>SHA-256 hex (64 chars) của artifact để device verify.</summary>
    public string Sha256Checksum { get; set; } = string.Empty;
    /// <summary>Kích thước artifact (bytes, &le; 50MB).</summary>
    public long ArtifactSizeBytes { get; set; }
    /// <summary>Tên file đã sanitize, lưu trong storage.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Phiên bản theo SemVer X.Y.Z.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string HardwareRevision { get; set; } = string.Empty;
    /// <summary>Force update — device bắt buộc apply.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public BatteryService.Domain.Enums.IotFirmwareChannelEnum Channel { get; set; } = BatteryService.Domain.Enums.IotFirmwareChannelEnum.Stable;
    /// <summary>Changelog/release notes (markdown).</summary>
    public string? ReleaseNotes { get; set; }
    /// <summary>Model device (vd "ESP32-S3-WROOM-1").</summary>
    public string? DeviceModel { get; set; }
}
