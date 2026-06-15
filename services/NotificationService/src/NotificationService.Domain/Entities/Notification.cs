using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Một bản ghi notification gửi tới 1 user qua 1 channel.
/// 1 nghiệp vụ (vd: ticket assigned) có thể fan-out thành nhiều Notification (push + email + in-app).
/// </summary>
public class Notification : AuditableEntity
{
    /// <summary>User nhận notification (AccountId từ AuthService).</summary>
    public Guid UserId { get; set; }

    public NotificationTypeEnum Type { get; set; }

    public NotificationChannelEnum Channel { get; set; }

    public NotificationStatusEnum Status { get; set; } = NotificationStatusEnum.Pending;

    /// <summary>Tiêu đề ngắn (push/email subject/in-app title).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Nội dung text plain (push body / in-app body).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>JSON payload bổ sung — deep link, entity ref, key-value cho client tự render.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Loại entity được liên kết (Ticket/Battery/...) — phục vụ group + filter.</summary>
    public string? EntityType { get; set; }

    /// <summary>Id entity liên kết.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Thời điểm send thành công xuống channel.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Thời điểm user mark read (chỉ ý nghĩa cho channel InApp/Push).</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>Lý do lỗi (nếu Status = Failed).</summary>
    public string? FailureReason { get; set; }
}
