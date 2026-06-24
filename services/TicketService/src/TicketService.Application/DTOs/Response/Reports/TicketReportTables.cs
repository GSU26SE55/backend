using SharedInfrastructure.Services;

namespace TicketService.Application.DTOs.Response.Reports;

/// <summary>Sprint 7 #114 (§5.3) — map report DTO → <see cref="ReportTable"/> để export CSV/XLSX.</summary>
public static class TicketReportTables
{
    public static ReportTable SlaByStaff(IEnumerable<SlaByStaffRow> rows) =>
        new("sla-by-staff",
            new[] { "StaffId", "Name", "TotalAssigned", "Met", "Breached", "ComplianceRate" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.StaffId, r.Name, r.TotalAssigned, r.Met, r.Breached, r.ComplianceRate }).ToList());

    public static ReportTable SlaByPriority(IEnumerable<SlaByPriorityRow> rows) =>
        new("sla-by-priority",
            new[] { "Priority", "Met", "Breached", "Total", "ComplianceRate" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.Priority, r.Met, r.Breached, r.Total, r.ComplianceRate }).ToList());

    public static ReportTable TimeSeries(string name, IEnumerable<TicketTimeSeriesPoint> rows) =>
        new(name,
            new[] { "Date", "Count" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Date, r.Count }).ToList());

    public static ReportTable TopReopen(IEnumerable<ReopenIssueRow> rows) =>
        new("top-reopen-issues",
            new[] { "Category", "Count", "AvgReopenCount" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Category, r.Count, r.AvgReopenCount }).ToList());

    public static ReportTable StaffPerformance(IEnumerable<StaffPerformanceRow> rows) =>
        new("staff-performance",
            new[] { "StaffId", "Name", "TicketsResolved", "AvgResolveHours", "AvgRating", "SlaCompliance" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.StaffId, r.Name, r.TicketsResolved, r.AvgResolveHours, r.AvgRating, r.SlaCompliance }).ToList());

    public static ReportTable Csat(CsatDto dto)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "AvgRating", dto.AvgRating },
            new object?[] { "TotalRated", dto.TotalRated }
        };
        for (var star = 1; star <= 5; star++)
        {
            rows.Add(new object?[] { $"Rating{star}", dto.RatingDistribution.TryGetValue(star, out var c) ? c : 0 });
        }
        return new ReportTable("csat", new[] { "Metric", "Value" }, rows);
    }

    public static ReportTable Histogram(IEnumerable<HistogramBucketRow> rows) =>
        new("resolution-time-histogram",
            new[] { "Bucket", "Count" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Bucket, r.Count }).ToList());

    public static ReportTable CategoryBreakdown(IEnumerable<CategoryBreakdownRow> rows) =>
        new("category-breakdown",
            new[] { "Category", "Count" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Category, r.Count }).ToList());

    public static ReportTable SagaFailedRate(IEnumerable<SagaFailedRatePoint> rows) =>
        new("saga-failed-rate",
            new[] { "Date", "Started", "Completed", "Failed", "FailedRate", "P95DurationSec" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
                { r.Date, r.Started, r.Completed, r.Failed, r.FailedRate, r.P95DurationSec }).ToList());
}
