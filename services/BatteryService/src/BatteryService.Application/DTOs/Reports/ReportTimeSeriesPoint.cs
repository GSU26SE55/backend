namespace BatteryService.Application.DTOs.Reports;

/// <summary>Sprint 7 #114 (§5.2) — điểm time-series (date + count).</summary>
public class ReportTimeSeriesPoint
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
