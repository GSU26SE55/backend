namespace BatteryService.Application.DTOs.Realtime;

/// <summary>
/// Sprint BE-IoT-Realtime (#614) — payload event SSE <c>reading</c> (§34.10.4).
/// Số đo ĐÃ làm sạch (calibration + loại outlier) — publish sau <c>SaveChangesAsync</c>.
/// Mọi message mang <c>BatteryAssetId</c> + <c>CustomerId</c>/<c>SiteId</c> để FE route đúng pin.
/// </summary>
public class LiveReadingDto
{
    public Guid BatteryAssetId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? BatteryTypeId { get; set; }
    public DateTime Time { get; set; }
    public decimal Voltage { get; set; }
    public decimal Current { get; set; }
    public decimal Temperature { get; set; }
    public decimal SocPercent { get; set; }
    public decimal? SohPercent { get; set; }
    public int? CycleCount { get; set; }
    public int? ChargingState { get; set; }
    public decimal? InternalResistanceMilliohm { get; set; }
    public decimal? CellVoltageDeltaMv { get; set; }
    public string? BmsErrorCode { get; set; }
    public string? SourceDeviceId { get; set; }
    public int SourceType { get; set; }

    /// <summary>"primary" | "redundant" | "external-temp" — FE mặc định vẽ <c>primary</c> (§34.10.5).</summary>
    public string? SensorSourceCode { get; set; }
}
