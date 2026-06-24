namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint 7 #118 — Application thấy interface, Infrastructure impl bằng prometheus-net.
/// Ghi metric khi detect environmental incident (smoke/water/...) phục vụ AlertManager rule.
/// </summary>
public interface IEnvironmentalMetricsRecorder
{
    /// <summary>
    /// Ghi nhận 1 incident được detect. <paramref name="detectionLatencySeconds"/> = khoảng cách
    /// từ lúc incident xảy ra (DetectedAt) đến lúc hệ thống ghi nhận (dùng cho alert latency).
    /// </summary>
    void IncidentDetected(string incidentType, string severity, double detectionLatencySeconds);
}
