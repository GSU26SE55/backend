namespace BatteryService.Application.DTOs;

/// <summary>SOH từ chuỗi dài (GH-10) — chỉ có SOH, không có anomaly/risk.</summary>
public class LongSohDto
{
    public string BatteryAssetId { get; set; } = string.Empty;

    public decimal SohPercent { get; set; }

    /// <summary>Số timestep AI thực sự chấm (có thể ít hơn Limit nếu pin chưa đủ dữ liệu).</summary>
    public int SeqLen { get; set; }

    /// <summary>"cpu" / "cuda".</summary>
    public string Device { get; set; } = string.Empty;

    public int LatencyMs { get; set; }

    /// <summary>
    /// Phiên bản model LONG — KHÁC modelVersion của SOH thường.
    /// </summary>
    /// <remarks>
    /// Hai đường dùng hai bộ trọng số riêng. Đừng vẽ chung một chart với SOH window=30 mà
    /// không ghi rõ nguồn: hai con số cho cùng một pin có thể lệch nhau đáng kể và đó là
    /// bình thường, không phải pin vừa thay đổi.
    /// </remarks>
    public string ModelVersion { get; set; } = string.Empty;
}

/// <summary>Một dòng kết quả trong lượt dự đoán hàng loạt.</summary>
public class BatchPredictionItemDto
{
    public string BatteryAssetId { get; set; } = string.Empty;
    public decimal SohPercent { get; set; }
    public string Classification { get; set; } = string.Empty;
    public string? HealthStage { get; set; }
    public string? RiskLevel { get; set; }
    public string? ActionCode { get; set; }
    public bool IsBorderline { get; set; }
    public bool IsTemperatureOod { get; set; }
}

/// <summary>Kết quả lượt dự đoán hàng loạt qua stream.</summary>
public class BatchPredictionDto
{
    public IReadOnlyList<BatchPredictionItemDto> Items { get; set; } = Array.Empty<BatchPredictionItemDto>();

    /// <summary>Số pin đã GỬI đi chấm.</summary>
    public int RequestedCount { get; set; }

    /// <summary>
    /// <c>false</c> khi stream đứt giữa chừng — những pin thiếu kết quả CHƯA được chấm.
    /// </summary>
    /// <remarks>
    /// ⚠️ Pin không có trong <see cref="Items"/> KHÔNG có nghĩa là pin đó bình thường.
    /// Bidi stream không có lỗi theo từng message: một cửa sổ sai làm đứt cả stream, nên
    /// hiển thị "không có cảnh báo" cho những pin đó là nói sai sự thật.
    /// </remarks>
    public bool IsComplete { get; set; }

    /// <summary>Lý do đứt; <c>null</c> nếu nhận đủ.</summary>
    public string? AbortReason { get; set; }
}
