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

    /// <summary>
    /// Sprint 6.4 NOTI4-06 — lần gửi đã sinh ra dòng này. Cho phép trả lời "thông báo X đã tới ai,
    /// bao nhiêu người đã đọc" — thứ trước đây không truy vấn được vì không có khoá gom nào.
    ///
    /// <para><b>Nullable có chủ đích</b>, ba nhóm dòng hợp lệ mà không thuộc lần gửi nào:
    /// <list type="bullet">
    /// <item>1.282 dòng đã có trước sprint này. Dữ liệu cũ KHÔNG có thông tin để gom thành lần gửi —
    /// gom theo thời gian là suy đoán và đã chứng minh là gom sai, nên để trống thay vì bịa (R-52).</item>
    /// <item><c>NotificationDigestBackgroundService</c> gộp nhiều thông báo thành một bản digest
    /// riêng cho từng người; bản digest tự sinh nội dung, không thuộc lần gửi nào.</item>
    /// <item><c>NotificationDispatcher</c> sinh dòng theo từng kênh trong lúc giao.</item>
    /// </list></para>
    /// </summary>
    public Guid? BatchId { get; set; }

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

    /// <summary>
    /// Sprint 6.2 NOTI-01 (#672) — số lần <c>NotificationDispatchBackgroundService</c> đã thử gửi
    /// record này xuống channel. Chạm ngưỡng <c>Notification:Dispatch:MaxAttempts</c> → chuyển
    /// <see cref="NotificationStatusEnum.Failed"/> (dừng retry vô hạn).
    /// </summary>
    public int DispatchAttemptCount { get; set; }

    /// <summary>
    /// Sprint 6.2 NOTI-01 (#672) — thời điểm sớm nhất worker được phép thử lại (UTC).
    /// Null = có thể gửi ngay. Dùng cho 2 trường hợp:
    /// <list type="bullet">
    /// <item>Backoff sau lần gửi lỗi.</item>
    /// <item>Hoãn tới hết quiet hours / hết cửa sổ digest — nếu để nguyên Pending không mốc thời gian
    /// thì batch (order by CreatedAt) sẽ bị các record hoãn chiếm chỗ, chặn record mới phía sau.</item>
    /// </list>
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// GH-792 — UTC, thời điểm bản ghi được CHIẾM để gửi
    /// (<see cref="NotificationStatusEnum.Processing"/>).
    /// </summary>
    /// <remarks>
    /// Dùng để thu hồi việc bị bỏ dở: tiến trình chết giữa chừng thì bản ghi nằm mãi ở
    /// <c>Processing</c> và không ai gửi nữa. Có mốc này mới phân biệt được "đang gửi" với "đã chết
    /// lúc đang gửi" — không có nó thì hoặc là kẹt vĩnh viễn, hoặc phải thu hồi mù và gửi trùng.
    /// </remarks>
    public DateTime? ProcessingStartedAt { get; set; }
}
