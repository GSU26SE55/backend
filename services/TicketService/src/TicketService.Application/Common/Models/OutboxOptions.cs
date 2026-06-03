namespace TicketService.Application.Common.Models;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int IntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
    public int MaxRetryCount { get; set; } = 5;
}
