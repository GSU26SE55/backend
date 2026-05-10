namespace SharedInfrastructure.Idempotency;

public class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public bool Enabled { get; set; } = true;
    public int TtlHours { get; set; } = 24;
}
