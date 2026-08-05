using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

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
    /// <para>
    /// Trước đây relay chỉ lọc <c>ProcessedAt == null</c>: hai replica cùng đọc được cùng một dòng
    /// và cùng publish, vì <c>ProcessedAt</c> chỉ được ghi SAU khi publish xong. Người dùng nhận
    /// email/SMS hai lần, và bản ghi outbox không lưu lại dấu vết nào của lần thứ hai.
    /// </para>
    /// <para>
    /// Cùng khuôn đã dùng ở TicketService (<c>OutboxClaimService</c>) — chép lại đúng cách làm đã
    /// chạy được thay vì nghĩ ra cách thứ hai cho cùng một bài toán.
    /// </para>
    /// </remarks>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// UTC — quyền giữ tới lúc nào. Hết hạn thì dòng lại nhận được, để một instance chết giữa chừng
    /// không khoá vĩnh viễn một message chưa ai gửi.
    /// </summary>
    public DateTime? LeaseUntilUtc { get; set; }
}
