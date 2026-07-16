namespace BatteryService.Application.DTOs;

public class SensorReadingAggregateDto
{
    /// <summary>Timestamp của reading (UTC).</summary>
    public DateTime Time { get; set; }

    /// <summary>Điện áp trung bình (V) trong bucket.</summary>
    public decimal AvgVoltage { get; set; }

    /// <summary>
    /// Dòng trung bình (A) trong bucket. ⚠️ Trộn dấu nạp/xả (giữ backward-compat cho FE cũ);
    /// vẽ chart tách chiều dùng <see cref="AvgChargeCurrent"/> / <see cref="AvgDischargeCurrent"/>.
    /// </summary>
    public decimal AvgCurrent { get; set; }

    /// <summary>Nhiệt độ trung bình (°C) trong bucket.</summary>
    public decimal AvgTemperature { get; set; }

    /// <summary>SOC trung bình (%) trong bucket.</summary>
    public decimal AvgSocPercent { get; set; }

    /// <summary>SOH trung bình (%) trong bucket (null nếu không có data).</summary>
    public decimal? AvgSohPercent { get; set; }

    // ── Sprint Bonus NS-02 (#647) — min/max nạp/xả tách chiều + V/T (Q4=A) ──
    // Chỉ tính trên reading source primary. Quy ước dấu: current > 0 = nạp, current < 0 = xả.
    // Min/max dòng LUÔN trả DƯƠNG (chiều nằm trong tên field). Nullable = bucket không có mẫu
    // chiều đó (0A là giá trị đo hợp lệ ≠ không có dữ liệu).

    /// <summary>MIN điện áp (V) trong bucket. Null nếu bucket không có reading primary.</summary>
    public decimal? MinVoltage { get; set; }

    /// <summary>MAX điện áp (V) trong bucket. Null nếu bucket không có reading primary.</summary>
    public decimal? MaxVoltage { get; set; }

    /// <summary>MIN nhiệt độ (°C) trong bucket. Null nếu bucket không có reading primary.</summary>
    public decimal? MinTemperature { get; set; }

    /// <summary>MAX nhiệt độ (°C) trong bucket. Null nếu bucket không có reading primary.</summary>
    public decimal? MaxTemperature { get; set; }

    /// <summary>Dòng nạp đỉnh (A, dương) — MAX(current) với current &gt; 0. Null nếu bucket không có mẫu nạp.</summary>
    public decimal? MaxChargeCurrent { get; set; }

    /// <summary>Dòng nạp thấp nhất khi đang nạp (A, dương) — MIN(current) với current &gt; 0. Null nếu không có mẫu nạp.</summary>
    public decimal? MinChargeCurrent { get; set; }

    /// <summary>AVG dòng nạp (A, dương). Null nếu bucket không có mẫu nạp.</summary>
    public decimal? AvgChargeCurrent { get; set; }

    /// <summary>Dòng xả đỉnh (A, dương) — MAX(ABS(current)) với current &lt; 0. Null nếu bucket không có mẫu xả.</summary>
    public decimal? MaxDischargeCurrent { get; set; }

    /// <summary>Dòng xả thấp nhất khi đang xả (A, dương) — MIN(ABS(current)) với current &lt; 0. Null nếu không có mẫu xả.</summary>
    public decimal? MinDischargeCurrent { get; set; }

    /// <summary>AVG dòng xả (A, dương). Null nếu bucket không có mẫu xả.</summary>
    public decimal? AvgDischargeCurrent { get; set; }

    /// <summary>Số mẫu chiều nạp (current &gt; 0) trong bucket.</summary>
    public int ChargeSampleCount { get; set; }

    /// <summary>Số mẫu chiều xả (current &lt; 0) trong bucket.</summary>
    public int DischargeSampleCount { get; set; }
}
