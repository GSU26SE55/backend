namespace SharedInfrastructure.Idempotency;

public class InboxOptions
{
    public const string SectionName = "Inbox";

    public int TtlDays { get; set; } = 7;
    public bool FailOpenWhenRedisDown { get; set; } = false;
}
