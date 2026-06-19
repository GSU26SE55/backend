namespace SmsService.Infrastructure.Options;

public class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Daily limit mặc định khi admin tạo device mới (Command default override được).</summary>
    public int DefaultDailyLimit { get; set; } = 100;

    /// <summary>(Legacy — không còn dùng sau khi loại bỏ FakeSmsSender). Giữ field để env config cũ không break.</summary>
    public string Provider { get; set; } = "Gateway";

    /// <summary>(Legacy — sender id, không dùng nữa).</summary>
    public string? From { get; set; }
}
