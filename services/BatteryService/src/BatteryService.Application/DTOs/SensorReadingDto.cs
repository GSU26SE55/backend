namespace BatteryService.Application.DTOs;

public class SensorReadingDto
{
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }

    /// <summary>ID BatteryAsset (Guid).</summary>
    public string BatteryAssetId { get; set; } = string.Empty;

    /// <summary>Điện áp (V).</summary>
    public decimal Voltage { get; set; }

    /// <summary>Cường độ dòng (A). Âm = xả, dương = sạc.</summary>
    public decimal Current { get; set; }

    /// <summary>Nhiệt độ (°C).</summary>
    public decimal Temperature { get; set; }

    /// <summary>State of Charge — % pin còn (0..100).</summary>
    public decimal SocPercent { get; set; }

    /// <summary>Số chu kỳ sạc/xả của pin.</summary>
    public int? CycleCount { get; set; }

    /// <summary>ID thiết bị nguồn (≤ 64 ký tự).</summary>
    public string? SourceDeviceId { get; set; }
}
