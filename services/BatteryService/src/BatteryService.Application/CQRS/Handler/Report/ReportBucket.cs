namespace BatteryService.Application.CQRS.Handler.Report;

/// <summary>Sprint 7 #114 — gom timestamp về mốc bucket theo granularity (day/week/month).</summary>
internal static class ReportBucket
{
    public static DateTime Of(DateTime dt, string granularity) => granularity?.ToLowerInvariant() switch
    {
        "month" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        "week" => DateTime.SpecifyKind(dt.Date.AddDays(-(int)dt.DayOfWeek), DateTimeKind.Utc),
        _ => DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc)   // day (default)
    };
}
