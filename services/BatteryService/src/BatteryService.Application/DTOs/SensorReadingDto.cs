using BatteryService.Domain.Enums;

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

    /// <summary>
    /// Các anomaly của CHÍNH dòng số đo này, chấm bằng <c>AnomalyRules.Detect</c> với
    /// ThresholdConfig của loại pin tương ứng.
    ///
    /// <para>Trước đây FE tự so số đo với ngưỡng rồi tự ghép nhãn ("Undervoltage 0.00V &lt; 10.50V").
    /// Đó là dựng lại luật của BE ở phía client: FE chỉ có 7 rule trong khi BE có 17 loại anomaly,
    /// severity không được tính, và hai bên lệch luật thì bảng bằng chứng hiển thị sai mà không ai
    /// biết. Nay BE chấm, FE chỉ hiển thị.</para>
    ///
    /// <para>Rỗng = dòng này nằm trong ngưỡng. Null-safe: nếu loại pin chưa cấu hình ngưỡng thì
    /// danh sách rỗng chứ không phải "không vi phạm".</para>
    /// </summary>
    public List<SensorReadingAnomalyDto> Anomalies { get; set; } = new();
}

/// <summary>
/// Một anomaly trên một dòng số đo. Đủ dữ liệu để FE dựng nhãn mà không cần biết luật:
/// loại, mức độ, ngưỡng bị vượt, giá trị đo được, đơn vị.
/// </summary>
public class SensorReadingAnomalyDto
{
    /// <summary>Loại anomaly (AnomalyTypeEnum) — BE serialize dạng string.</summary>
    public AnomalyTypeEnum Type { get; set; }

    /// <summary>Mức độ (AlertSeverityEnum) — Warning hay Critical.</summary>
    public AlertSeverityEnum Severity { get; set; }

    /// <summary>Ngưỡng bị vượt, lấy từ ThresholdConfig của loại pin.</summary>
    public decimal ThresholdValue { get; set; }

    /// <summary>Giá trị đo được thực tế.</summary>
    public decimal ActualValue { get; set; }

    /// <summary>Đơn vị hiển thị: "V", "A", "°C", "%".</summary>
    public string Unit { get; set; } = string.Empty;
}
