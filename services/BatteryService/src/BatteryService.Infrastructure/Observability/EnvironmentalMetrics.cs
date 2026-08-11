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
        "Total number of environmental incidents detected/reported.",
        new CounterConfiguration { LabelNames = new[] { "type", "severity" } });

    public static readonly Histogram DetectionLatencySeconds = Metrics.CreateHistogram(
        "environmental_incident_detection_latency_seconds",
        "Latency from when the incident occurred (DetectedAt) until the system recorded it.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(1, 2, 12) });
}
