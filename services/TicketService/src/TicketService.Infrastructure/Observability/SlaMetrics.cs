using Prometheus;

namespace TicketService.Infrastructure.Observability;

/// <summary>
/// Sprint 7 #117 — Prometheus gauge SLA AGGREGATE. Dashboard Grafana "SLA Ops".
/// Cardinality thấp: compliance ratio (no label) + breached count theo priority (3 label P1/P2/P3).
/// </summary>
public static class SlaMetrics
{
    public static readonly Gauge ComplianceRatio = Metrics.CreateGauge(
        "ticket_sla_compliance_ratio",
        "Tỷ lệ SLA tuân thủ toàn hệ (Met / (Met + Breached)), 0.0–1.0.");

    public static readonly Gauge BreachedCount = Metrics.CreateGauge(
        "ticket_sla_breached_count",
        "Số SLA timer breached theo priority.",
        new GaugeConfiguration { LabelNames = new[] { "priority" } });
}
