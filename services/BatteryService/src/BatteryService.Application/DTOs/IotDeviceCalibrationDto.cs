namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint IoT-2 #IoT2-32 — DTO calibration profile của 1 channel.
/// </summary>
public class IotDeviceCalibrationDto
{
    /// <summary>Định danh resource.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>ID IoT device (Guid).</summary>
    public string IotDeviceId { get; set; } = string.Empty;
    /// <summary>Channel sensor (vd "voltage", "current", "temperature").</summary>
    public string Channel { get; set; } = string.Empty;
    /// <summary>ID BatteryAsset (Guid).</summary>
    public string? BatteryAssetId { get; set; }
    /// <summary>Scale factor calibration (default 1.0).</summary>
    public decimal Scale { get; set; }
    /// <summary>Offset cộng thêm calibration (default 0.0).</summary>
    public decimal Offset { get; set; }
    /// <summary>Đơn vị đo (V | A | °C | %).</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>Ngày calibration thực hiện.</summary>
    public DateTime CalibratedAt { get; set; }
    /// <summary>Ngày hết hạn.</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }
    /// <summary>Timestamp tạo (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
