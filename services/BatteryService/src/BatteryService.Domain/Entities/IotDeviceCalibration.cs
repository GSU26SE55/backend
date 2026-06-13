using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-1 (#242, #247) — calibration profile cho 1 sensor channel của <see cref="IotDevice"/>.
/// Backend áp dụng <c>raw_value * Scale + Offset</c> trước khi lưu vào sensor_readings.
/// 1 device có thể có nhiều calibration entries (mỗi entry 1 channel: voltage/current/temperature/...).
/// </summary>
public class IotDeviceCalibration : AuditableEntity
{
    public Guid IotDeviceId { get; set; }

    public IotDevice IotDevice { get; set; } = null!;

    /// <summary>Channel ngắn ("voltage", "current", "temperature"). Khớp với SensorReading field.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>BatteryAsset cụ thể nếu device đo nhiều pin (nullable cho calibration scope device-level).</summary>
    public Guid? BatteryAssetId { get; set; }

    /// <summary>Hệ số nhân raw → engineering unit. Default 1.0.</summary>
    public decimal Scale { get; set; } = 1m;

    /// <summary>Hệ số cộng raw → engineering unit. Default 0.0.</summary>
    public decimal Offset { get; set; }

    /// <summary>Đơn vị engineering output ("V", "A", "°C").</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Ngày calibration lấy mẫu vật lý. Manager đối chứng với calibrator chuẩn.</summary>
    public DateTime CalibratedAt { get; set; }

    /// <summary>Ngày calibration hết hạn — nên recalibrate.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string? Notes { get; set; }
}
