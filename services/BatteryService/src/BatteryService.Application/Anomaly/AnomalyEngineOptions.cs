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
}
