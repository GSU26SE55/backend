using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Reports;

/// <summary>
/// Sprint 7 #114 — helper chung cho report handlers.
/// "Resolved" = Status ∈ {Resolved, ClosedPendingRate, Closed}. SLA Met/Breached từ SlaTimerStatusEnum.
/// </summary>
internal static class TicketReportHelpers
{
    public static readonly TicketStatusEnum[] ResolvedStatuses =
        { TicketStatusEnum.Resolved, TicketStatusEnum.ClosedPendingRate, TicketStatusEnum.Closed };

    public static DateTime Bucket(DateTime dt, string granularity) => granularity?.ToLowerInvariant() switch
    {
        "month" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        "week" => DateTime.SpecifyKind(dt.Date.AddDays(-(int)dt.DayOfWeek), DateTimeKind.Utc),
        _ => DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc)
    };

    public static decimal Compliance(int met, int breached) =>
        (met + breached) == 0 ? 0m : Math.Round(met * 100m / (met + breached), 2);
}
