namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-32 — DTO calibration profile của 1 channel.
/// </summary>
public class IotDeviceCalibrationDto
{
    public string Id { get; set; } = string.Empty;
    public string IotDeviceId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? BatteryAssetId { get; set; }
    public decimal Scale { get; set; }
    public decimal Offset { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTime CalibratedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
