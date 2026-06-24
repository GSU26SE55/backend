using Prometheus;

namespace BatteryService.Infrastructure.Observability;

/// <summary>
/// Sprint 7 #118 — Prometheus metrics cho environmental incident (smoke/water/over-temp...).
/// Scrape qua /metrics. Dùng bởi AlertManager rule group "environmental-monitoring".
/// </summary>
public static class EnvironmentalMetrics
{
    public static readonly Counter IncidentsDetectedTotal = Metrics.CreateCounter(
        "environmental_incident_detected_total",
        "Tổng số environmental incident được detect/report.",
        new CounterConfiguration { LabelNames = new[] { "type", "severity" } });

    public static readonly Histogram DetectionLatencySeconds = Metrics.CreateHistogram(
        "environmental_incident_detection_latency_seconds",
        "Độ trễ từ lúc incident xảy ra (DetectedAt) đến lúc hệ thống ghi nhận.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(1, 2, 12) });
}
