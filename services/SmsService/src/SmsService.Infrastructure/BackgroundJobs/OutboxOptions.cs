namespace SmsService.Infrastructure.BackgroundJobs;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 50;
    public int PollIntervalSeconds { get; set; } = 2;
    public int MaxRetries { get; set; } = 10;
}
