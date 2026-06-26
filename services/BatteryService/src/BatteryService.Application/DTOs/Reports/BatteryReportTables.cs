using SharedInfrastructure.Services;

namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.3) — map mỗi report DTO → <see cref="ReportTable"/> để export CSV/XLSX.</summary>
public static class BatteryReportTables
{
    public static ReportTable BatteryHealthByType(IEnumerable<BatteryHealthByTypeRow> rows) =>
        new("battery-health-by-type",
            new[] { "TypeId", "Name", "TotalAssets", "WithActiveAlerts", "HealthScore" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.TypeId, r.Name, r.TotalAssets, r.WithActiveAlerts, r.HealthScore }).ToList());

    public static ReportTable TimeSeries(string name, IEnumerable<ReportTimeSeriesPoint> rows) =>
        new(name,
            new[] { "Date", "Count" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Date, r.Count }).ToList());

    public static ReportTable TopAnomalies(IEnumerable<TopAnomalyRow> rows) =>
        new("top-anomalies",
            new[] { "AnomalyType", "Count", "CriticalCount" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.AnomalyType, r.Count, r.CriticalCount }).ToList());

    public static ReportTable AssetLifecycle(IEnumerable<AssetLifecycleRow> rows) =>
        new("asset-lifecycle",
            new[] { "AssetId", "SerialNumber", "AgeDays", "CycleCount", "AlertsTotal" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.AssetId, r.SerialNumber, r.AgeDays, r.CycleCount, r.AlertsTotal }).ToList());

    public static ReportTable WarrantyExpiring(IEnumerable<WarrantyExpiringRow> rows) =>
        new("warranty-expiring",
            new[] { "AssetId", "SerialNumber", "WarrantyEndDate", "DaysRemaining", "CustomerId" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.AssetId, r.SerialNumber, r.WarrantyEndDate, r.DaysRemaining, r.CustomerId }).ToList());

    public static ReportTable EnvironmentalIncidents(IEnumerable<EnvironmentalIncidentRow> rows) =>
        new("environmental-incidents",
            new[] { "SiteId", "IncidentType", "Severity", "DetectedAt", "ResolvedAt", "DurationHours", "WasFalseAlarm" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.SiteId, r.IncidentType, r.Severity, r.DetectedAt, r.ResolvedAt, r.DurationHours, r.WasFalseAlarm }).ToList());

    public static ReportTable AmbientTrend(IEnumerable<AmbientTrendPoint> rows) =>
        new("ambient-trend",
            new[] { "Date", "AvgTemp", "MaxTemp", "MinTemp", "HumidityAvg", "IrradianceAvg" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.Date, r.AvgTemp, r.MaxTemp, r.MinTemp, r.HumidityAvg, r.IrradianceAvg }).ToList());
}
