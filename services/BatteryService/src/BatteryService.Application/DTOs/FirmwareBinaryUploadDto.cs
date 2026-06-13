namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-35 — response sau khi upload .bin qua multipart.
/// Admin dùng <c>ArtifactUrl</c> + <c>Sha256Checksum</c> + <c>ArtifactSizeBytes</c>
/// để gọi tiếp <c>POST /api/admin/iot-firmware-releases</c> tạo metadata release.
/// </summary>
public class FirmwareBinaryUploadDto
{
    public string ArtifactUrl { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long ArtifactSizeBytes { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string HardwareRevision { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public BatteryService.Domain.Enums.IotFirmwareChannelEnum Channel { get; set; } = BatteryService.Domain.Enums.IotFirmwareChannelEnum.Stable;
    public string? ReleaseNotes { get; set; }
    public string? DeviceModel { get; set; }
}
