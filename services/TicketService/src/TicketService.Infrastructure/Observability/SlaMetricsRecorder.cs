using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Observability;

/// <summary>Sprint 7 #117 — bridge Application → prometheus-net cho SLA gauge.</summary>
internal sealed class SlaMetricsRecorder : ISlaMetricsRecorder
{
    public void SetSlaGauges(double complianceRatio, IReadOnlyDictionary<string, int> breachedByPriority)
    {
        SlaMetrics.ComplianceRatio.Set(complianceRatio);

        foreach (var kv in breachedByPriority)
        {
            SlaMetrics.BreachedCount.WithLabels(kv.Key).Set(kv.Value);
        }
    }
}
