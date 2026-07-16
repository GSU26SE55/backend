namespace BatteryService.Application.DTOs.Realtime;

/// <summary>
/// Sprint Bonus NS-03/NS-04 (#648/#649) — payload event SSE <c>stats</c> (§5.3bis docs realtime).
/// Rolling min/max dòng nạp/xả của 1 pin trong 1 window (<c>1h</c> / <c>today</c>).
/// Field min/max nullable + LUÔN dương; null bị lược khỏi JSON (JsonOptions WhenWritingNull).
/// </summary>
public class LiveStatsDto
{
    public Guid BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? SiteId { get; set; }

    /// <summary>"1h" | "today".</summary>
    public string Window { get; set; } = string.Empty;

    /// <summary>Mốc bắt đầu window (UTC).</summary>
    public DateTime WindowStart { get; set; }

    public decimal? MaxChargeCurrent { get; set; }
    public decimal? MinChargeCurrent { get; set; }
    public decimal? MaxDischargeCurrent { get; set; }
    public decimal? MinDischargeCurrent { get; set; }

    public int ChargeSampleCount { get; set; }
    public int DischargeSampleCount { get; set; }

    /// <summary>Thời điểm cập nhật cuối (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
