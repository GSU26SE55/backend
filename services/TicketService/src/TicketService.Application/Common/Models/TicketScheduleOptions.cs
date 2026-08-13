namespace TicketService.Application.Common.Models;

public sealed class TicketScheduleOptions
{
    public const string SectionName = "Ticket:Schedule";
    public int CurrentWindowMinutes { get; set; } = 5;
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 50;
}
