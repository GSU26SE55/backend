namespace BatteryService.Application.Anomaly;

/// <summary>
/// Config cho anomaly engine. Bind từ <c>"AnomalyEngine"</c> section trong appsettings.
/// </summary>
public class AnomalyEngineOptions
{
    public const string SectionName = "AnomalyEngine";

    public int ScanIntervalSeconds { get; set; } = 30;
    public int DedupWindowMinutes { get; set; } = 30;
    public int OfflineThresholdMinutes { get; set; } = 10;
    public int EscalationAfterMinutes { get; set; } = 5;
    public int EscalationIntervalSeconds { get; set; } = 60;
    public int OutboxRelayIntervalSeconds { get; set; } = 5;
    public int OutboxRelayBatchSize { get; set; } = 100;

    /// <summary>
    /// Sprint 5B B10 (#158) — auto-resolve Open alerts khi anomaly không còn xuất hiện
    /// trong cửa sổ lookback.
    /// </summary>
    public int AutoResolveIntervalSeconds { get; set; } = 300;
    public int AutoResolveLookbackMinutes { get; set; } = 10;
    public int AutoResolveBatchSize { get; set; } = 100;
}
