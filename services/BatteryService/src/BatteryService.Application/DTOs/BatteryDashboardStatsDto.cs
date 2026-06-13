using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint 5B B11 — Battery dashboard payload phục vụ Admin/Manager UI:
/// KPI cards + nhiều biểu đồ (donut/bar/line/heatmap) chỉ qua 1 endpoint.
/// </summary>
public class BatteryDashboardStatsDto
{
    // ===== KPI counters =====
    public int TotalAssets { get; set; }
    public int ActiveAssets { get; set; }
    public int OfflineAssets { get; set; }
    public int OpenAlerts { get; set; }
    public int OpenAlertsCritical { get; set; }
    public int OpenAlertsWarning { get; set; }
    public int OpenEnvironmentalIncidents { get; set; }
    public int Sites { get; set; }

    // ===== Donut: Asset status distribution =====
    public List<AssetStatusBucketDto> AssetStatusDistribution { get; set; } = new();

    // ===== Donut: SOH bucket =====
    public SohBucketDto SohDistribution { get; set; } = new();

    // ===== Donut: Alert by severity (toàn bộ status — Open/Ack/Resolved) =====
    public AlertSeverityBreakdownDto AlertSeverityBreakdown { get; set; } = new();

    // ===== Bar: Alert by anomaly type (chỉ Open) =====
    public List<AlertByTypeDto> OpenAlertsByType { get; set; } = new();

    // ===== Line: Alert trend 7 ngày qua =====
    public List<DailyTrendPointDto> AlertTrend7Days { get; set; } = new();

    // ===== Line: Ambient temperature 24h gần nhất =====
    public List<AmbientTrendPointDto> AmbientTrend24Hours { get; set; } = new();

    // ===== Aggregated sensor metrics 24h gần nhất =====
    public SensorAggregateDto SensorAggregate24Hours { get; set; } = new();

    // ===== Top 5 asset có nhiều alert nhất (30 ngày) =====
    public List<TopAlertingAssetDto> TopAlertingAssets { get; set; } = new();

    // ===== Donut: Environmental incident by type (Open + Acknowledged) =====
    public List<EnvironmentalIncidentByTypeDto> EnvironmentalIncidentsByType { get; set; } = new();

    // ===== Donut: Battery chemistry distribution =====
    public List<ChemistryBucketDto> ChemistryDistribution { get; set; } = new();
}

public class AssetStatusBucketDto
{
    public BatteryStatusEnum Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SohBucketDto
{
    /// <summary>SOH ≥ 90% — pin khoẻ.</summary>
    public int Healthy { get; set; }
    /// <summary>SOH 80–89% — bình thường.</summary>
    public int Normal { get; set; }
    /// <summary>SOH 75–79% — cảnh báo.</summary>
    public int Warning { get; set; }
    /// <summary>SOH &lt; 75% — gần EOL/EOL.</summary>
    public int Eol { get; set; }
    /// <summary>Asset chưa có sensor reading SOH.</summary>
    public int Unknown { get; set; }
}

public class AlertSeverityBreakdownDto
{
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
}

public class AlertByTypeDto
{
    public AnomalyTypeEnum AnomalyType { get; set; }
    public string AnomalyName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class DailyTrendPointDto
{
    public DateOnly Date { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
    public int Total { get; set; }
}

public class AmbientTrendPointDto
{
    public DateTime HourUtc { get; set; }
    public decimal? AvgTemperature { get; set; }
    public decimal? AvgHumidity { get; set; }
    public decimal? AvgSolarIrradiance { get; set; }
}

public class SensorAggregateDto
{
    public decimal? AvgVoltage { get; set; }
    public decimal? AvgCurrent { get; set; }
    public decimal? AvgTemperature { get; set; }
    public decimal? AvgSoc { get; set; }
    public decimal? AvgSoh { get; set; }
    public int ReadingsCount { get; set; }
}

public class TopAlertingAssetDto
{
    public Guid BatteryAssetId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public int AlertCount { get; set; }
    public int CriticalCount { get; set; }
}

public class EnvironmentalIncidentByTypeDto
{
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    public string IncidentName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ChemistryBucketDto
{
    public BatteryChemistryEnum Chemistry { get; set; }
    public string ChemistryName { get; set; } = string.Empty;
    public int AssetCount { get; set; }
}
