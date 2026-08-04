using Prometheus;

namespace BatteryService.Infrastructure.Observability;

/// <summary>
/// Sprint BE-IoT-Realtime (#622) — metrics kênh SSE telemetry (§34.10 / BEIOT-RT-09).
/// Pattern theo <c>IotMetrics</c> (prometheus-net static counters/gauges).
/// </summary>
public static class RealtimeMetrics
{
    /// <summary>Số kết nối SSE đang mở (gauge) — inc khi subscribe, dec khi disconnect.</summary>
    public static readonly Gauge ActiveConnections = Metrics.CreateGauge(
        "sse_active_connections",
        "Số kết nối SSE telemetry đang mở.",
        new GaugeConfiguration { LabelNames = new[] { "scope" } });

    /// <summary>Tổng số event SSE đã đẩy xuống client (counter), label theo loại event.</summary>
    public static readonly Counter EventsPushed = Metrics.CreateCounter(
        "sse_events_pushed_total",
        "Tổng số event SSE telemetry đã đẩy. Label scope: asset|customer|site; event: reading|summary|stats|ping.",
        new CounterConfiguration { LabelNames = new[] { "scope", "event" } });
}
