namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Sprint 7 #117 — set aggregate gauge SLA (toàn hệ, low cardinality) cho Grafana "SLA Ops".
/// Detail per-staff thuộc web app qua Reports API #114. Refresh bởi SlaGaugeBackgroundService.
/// </summary>
public interface ISlaMetricsRecorder
{
    void SetSlaGauges(double complianceRatio, IReadOnlyDictionary<string, int> breachedByPriority);
}
