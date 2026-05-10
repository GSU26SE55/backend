namespace SmsService.Infrastructure.Options;

public class SmsOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; set; } = "Fake";

    public string? From { get; set; }
}
