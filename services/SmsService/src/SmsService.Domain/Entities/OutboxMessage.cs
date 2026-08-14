using SharedKernels.Domain;

namespace SmsService.Domain.Entities;

/// <summary>
/// Bản ghi Outbox Pattern — INSERT cùng transaction với business data trong handler,
/// <c>OutboxRelayBackgroundService</c> poll và publish ra RabbitMQ sau.
/// </summary>
public class OutboxMessage : AuditableEntity
{
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// GH-794 — instance đang giữ quyền publish dòng này.
    /// </summary>
    /// <remarks>
    /// Trước đây relay chỉ lọc <c>ProcessedAt == null</c>: hai replica cùng đọc được cùng một dòng
    /// và cùng publish, vì <c>ProcessedAt</c> chỉ được ghi SAU khi publish xong. Với SMS thì mỗi lần
    /// trùng là một tin nhắn tính phí gửi thêm cho người dùng.
    /// </remarks>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// UTC — quyền giữ tới lúc nào. Hết hạn thì dòng lại nhận được, để một instance chết giữa chừng
    /// không khoá vĩnh viễn một message chưa ai gửi.
    /// </summary>
    public DateTime? LeaseUntilUtc { get; set; }
}
