using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SlaBreachedEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime BreachedAt { get; init; }
    public string Priority { get; init; } = string.Empty;
}

public record SlaWarningEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime WarningAt { get; init; }
    public double Percentage { get; init; }

    /// <summary>
    /// Sprint 6.2 NOTI-05 (#676) — Staff đang được assign ticket. Spec §3.4 yêu cầu SLA warning
    /// báo cả Staff phụ trách lẫn Manager; trước đó payload không có StaffId nên consumer chỉ
    /// broadcast Manager được (reviewnotification.md §4.2). Null = ticket chưa assign ai.
    /// </summary>
    public Guid? StaffId { get; init; }
}
