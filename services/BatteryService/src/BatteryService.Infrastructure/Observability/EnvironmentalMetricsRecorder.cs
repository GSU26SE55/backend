using BatteryService.Application.Interfaces;

namespace BatteryService.Infrastructure.Observability;

/// <summary>Sprint 7 #118 — bridge Application → prometheus-net cho environmental incident.</summary>
internal sealed class EnvironmentalMetricsRecorder : IEnvironmentalMetricsRecorder
{
    public void IncidentDetected(string incidentType, string severity, double detectionLatencySeconds)
    {
        EnvironmentalMetrics.IncidentsDetectedTotal.WithLabels(incidentType, severity).Inc();

        if (detectionLatencySeconds >= 0)
        {
            EnvironmentalMetrics.DetectionLatencySeconds.Observe(detectionLatencySeconds);
        }
    }
}
