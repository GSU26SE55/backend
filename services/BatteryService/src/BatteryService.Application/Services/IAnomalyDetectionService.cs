namespace BatteryService.Application.Services;

/// <summary>
/// Sweep sensor readings → detect anomaly → tạo/merge Alert → ghi Outbox event nếu Critical.
/// Background-only service, không expose qua REST (không cần CQRS wrapper).
/// </summary>
public interface IAnomalyDetectionService
{
    Task<AnomalyDetectionResult> ScanRecentReadingsAsync(
        TimeSpan lookbackWindow,
        CancellationToken cancellationToken = default);
}

public class AnomalyDetectionResult
{
    public int ReadingsScanned { get; set; }
    public int AlertsCreated { get; set; }
    public int AlertsMerged { get; set; }
    public int OutboxEventsQueued { get; set; }

    /// <summary>Sprint 5B B1 (#152) — số anomaly bị noise-suppress (chưa đủ frequency).</summary>
    public int AlertsSuppressed { get; set; }
}
