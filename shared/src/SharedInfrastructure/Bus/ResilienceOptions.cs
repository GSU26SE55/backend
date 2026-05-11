namespace SharedInfrastructure.Bus;

/// <summary>
/// Cấu hình resilience policy cho MassTransit consumer.
///
/// Bind từ section <c>Resilience</c> trong appsettings hoặc env var <c>Resilience__...</c>:
/// <code>
/// Resilience__MessageRetry__Limit=5
/// Resilience__MessageRetry__MinIntervalSeconds=1
/// Resilience__MessageRetry__MaxIntervalSeconds=30
/// Resilience__MessageRetry__IntervalDeltaSeconds=2
/// Resilience__ScheduledRedelivery__Enabled=true
/// Resilience__ScheduledRedelivery__IntervalsMinutes=5,15,30
/// </code>
///
/// Defaults được chọn cho dự án GSU26SE55 — phù hợp transient failure ngắn (network blip, DB lock).
/// Long-outage được handle qua <see cref="ScheduledRedeliveryOptions"/> để tránh giữ thread consumer.
/// </summary>
public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public MessageRetryOptions MessageRetry { get; set; } = new();

    public ScheduledRedeliveryOptions ScheduledRedelivery { get; set; } = new();
}

/// <summary>
/// In-process retry (Polly-like) chạy ngay trong consumer mà KHÔNG release message khỏi queue.
/// Phù hợp transient failure < 30s (DB lock, network blip).
/// Sau khi exhausted → fall back sang <see cref="ScheduledRedeliveryOptions"/> nếu bật.
/// </summary>
public class MessageRetryOptions
{
    /// <summary>Số lần retry tối đa (không tính lần đầu). Default 5.</summary>
    public int Limit { get; set; } = 5;

    /// <summary>Khoảng cách RETRY ĐẦU TIÊN (giây). Default 1s.</summary>
    public int MinIntervalSeconds { get; set; } = 1;

    /// <summary>Trần khoảng cách giữa các retry (giây). Default 30s.</summary>
    public int MaxIntervalSeconds { get; set; } = 30;

    /// <summary>Bước tăng mỗi lần (giây) cho exponential. Default 2s → 1, 3, 7, 15, 30, 30...</summary>
    public int IntervalDeltaSeconds { get; set; } = 2;
}

/// <summary>
/// Long-term retry: sau khi <see cref="MessageRetryOptions"/> exhausted, message được "ngủ"
/// trong queue delayed-exchange và quay lại consumer sau N phút.
/// Phù hợp outage dài (mailjet down 30 phút, RabbitMQ peer disconnect).
/// </summary>
public class ScheduledRedeliveryOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// CSV danh sách interval (phút) cho mỗi lần redelivery, vd "5,15,30".
    /// Default "5,15,30" → retry sau 5p, 15p, 30p sau khi MessageRetry exhausted.
    /// Sau khi cạn list → message tự move vào DLQ <c>{queue}_error</c>.
    ///
    /// Lý do dùng CSV thay vì <c>int[]</c>: .NET Configuration binder MERGE array
    /// (defaults + override = concat) thay vì REPLACE → bug behavior. CSV parse manual tránh.
    /// </summary>
    public string IntervalsMinutes { get; set; } = "5,15,30";

    /// <summary>Parse <see cref="IntervalsMinutes"/> CSV thành int[] hợp lệ (positive only, skip invalid).</summary>
    public int[] GetIntervalsMinutes()
    {
        if (string.IsNullOrWhiteSpace(IntervalsMinutes))
            return Array.Empty<int>();

        return IntervalsMinutes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToArray();
    }
}
