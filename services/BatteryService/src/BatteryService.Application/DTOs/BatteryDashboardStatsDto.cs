using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

/// <summary>
/// Sprint 5B B11 — Battery dashboard payload phục vụ Admin/Manager UI:
/// KPI cards + nhiều biểu đồ (donut/bar/line/heatmap) chỉ qua 1 endpoint.
/// </summary>
public class BatteryDashboardStatsDto
{
    // ===== KPI counters =====
    /// <summary>Tổng số asset trong scope.</summary>
    public int TotalAssets { get; set; }
    /// <summary>Số asset đang active.</summary>
    public int ActiveAssets { get; set; }
    /// <summary>Số asset offline.</summary>
    public int OfflineAssets { get; set; }
    /// <summary>Số alert đang open.</summary>
    public int OpenAlerts { get; set; }
    /// <summary>Số alert open severity Critical.</summary>
    public int OpenAlertsCritical { get; set; }
    /// <summary>Số alert open severity Warning.</summary>
    public int OpenAlertsWarning { get; set; }
    /// <summary>Số env incident open.</summary>
    public int OpenEnvironmentalIncidents { get; set; }
    /// <summary>Tổng số site customer sở hữu.</summary>
    public int Sites { get; set; }

    // ===== Donut: Asset status distribution =====
    /// <summary>Phân phối asset theo Status.</summary>
    public List<AssetStatusBucketDto> AssetStatusDistribution { get; set; } = new();

    // ===== Donut: SOH bucket =====
    /// <summary>Phân phối asset theo SOH bucket.</summary>
    public SohBucketDto SohDistribution { get; set; } = new();

    // ===== Donut: Alert by severity (toàn bộ status — Open/Ack/Resolved) =====
    /// <summary>Field AlertSeverityBreakdown.</summary>
    public AlertSeverityBreakdownDto AlertSeverityBreakdown { get; set; } = new();

    // ===== Bar: Alert by anomaly type (chỉ Open) =====
    /// <summary>Field OpenAlertsByType.</summary>
    public List<AlertByTypeDto> OpenAlertsByType { get; set; } = new();

    // ===== Line: Alert trend 7 ngày qua =====
    /// <summary>Field AlertTrend7Days.</summary>
    public List<DailyTrendPointDto> AlertTrend7Days { get; set; } = new();

    // ===== Line: Ambient temperature 24h gần nhất =====
    /// <summary>Field AmbientTrend24Hours.</summary>
    public List<AmbientTrendPointDto> AmbientTrend24Hours { get; set; } = new();

    // ===== Aggregated sensor metrics 24h gần nhất =====
    /// <summary>Field SensorAggregate24Hours.</summary>
    public SensorAggregateDto SensorAggregate24Hours { get; set; } = new();

    // ===== Top 5 asset có nhiều alert nhất (30 ngày) =====
    /// <summary>Field TopAlertingAssets.</summary>
    public List<TopAlertingAssetDto> TopAlertingAssets { get; set; } = new();

    // ===== Donut: Environmental incident by type (Open + Acknowledged) =====
    /// <summary>Field EnvironmentalIncidentsByType.</summary>
    public List<EnvironmentalIncidentByTypeDto> EnvironmentalIncidentsByType { get; set; } = new();

    // ===== Donut: Battery chemistry distribution =====
    /// <summary>Field ChemistryDistribution.</summary>
    public List<ChemistryBucketDto> ChemistryDistribution { get; set; } = new();
}

public class AssetStatusBucketDto
{
    /// <summary>Filter theo status enum.</summary>
    public BatteryStatusEnum Status { get; set; }
    /// <summary>Tên của status.</summary>
    public string StatusName { get; set; } = string.Empty;
    /// <summary>Số lượng.</summary>
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
    /// <summary>Field Critical.</summary>
    public int Critical { get; set; }
    /// <summary>Field Warning.</summary>
    public int Warning { get; set; }
    /// <summary>Field Info.</summary>
    public int Info { get; set; }
}

public class AlertByTypeDto
{
    /// <summary>Loại bất thường (xem AnomalyTypeEnum 1..15).</summary>
    public AnomalyTypeEnum AnomalyType { get; set; }
    /// <summary>Tên của anomaly.</summary>
    public string AnomalyName { get; set; } = string.Empty;
    /// <summary>Số lượng.</summary>
    public int Count { get; set; }
}

public class DailyTrendPointDto
{
    /// <summary>Field Date.</summary>
    public DateOnly Date { get; set; }
    /// <summary>Field Critical.</summary>
    public int Critical { get; set; }
    /// <summary>Field Warning.</summary>
    public int Warning { get; set; }
    /// <summary>Field Info.</summary>
    public int Info { get; set; }
    /// <summary>Field Total.</summary>
    public int Total { get; set; }
}

public class AmbientTrendPointDto
{
    /// <summary>Field HourUtc.</summary>
    public DateTime HourUtc { get; set; }
    /// <summary>Nhiệt độ trung bình (°C) trong bucket.</summary>
    public decimal? AvgTemperature { get; set; }
    /// <summary>Field AvgHumidity.</summary>
    public decimal? AvgHumidity { get; set; }
    /// <summary>Field AvgSolarIrradiance.</summary>
    public decimal? AvgSolarIrradiance { get; set; }
}

public class SensorAggregateDto
{
    /// <summary>Điện áp trung bình (V) trong bucket.</summary>
    public decimal? AvgVoltage { get; set; }
    /// <summary>Dòng trung bình (A) trong bucket.</summary>
    public decimal? AvgCurrent { get; set; }
    /// <summary>Nhiệt độ trung bình (°C) trong bucket.</summary>
    public decimal? AvgTemperature { get; set; }
    /// <summary>Field AvgSoc.</summary>
    public decimal? AvgSoc { get; set; }
    /// <summary>Field AvgSoh.</summary>
    public decimal? AvgSoh { get; set; }
    /// <summary>Số lượng.</summary>
    public int ReadingsCount { get; set; }
}

public class TopAlertingAssetDto
{
    /// <summary>ID BatteryAsset (Guid).</summary>
    public Guid BatteryAssetId { get; set; }
    /// <summary>Serial number của asset (unique).</summary>
    public string SerialNumber { get; set; } = string.Empty;
    /// <summary>Số lượng.</summary>
    public int AlertCount { get; set; }
    /// <summary>Số lượng.</summary>
    public int CriticalCount { get; set; }
}

public class EnvironmentalIncidentByTypeDto
{
    /// <summary>Loại incident (SmokeDetected | WaterLeak | ...).</summary>
    public EnvironmentalIncidentTypeEnum IncidentType { get; set; }
    /// <summary>Tên của incident.</summary>
    public string IncidentName { get; set; } = string.Empty;
    /// <summary>Số lượng.</summary>
    public int Count { get; set; }
}

public class ChemistryBucketDto
{
    /// <summary>Hoá học pin (LiFePO4 | NMC | NCA).</summary>
    public BatteryChemistryEnum Chemistry { get; set; }
    /// <summary>Tên của chemistry.</summary>
    public string ChemistryName { get; set; } = string.Empty;
    /// <summary>Số lượng.</summary>
    public int AssetCount { get; set; }
}
