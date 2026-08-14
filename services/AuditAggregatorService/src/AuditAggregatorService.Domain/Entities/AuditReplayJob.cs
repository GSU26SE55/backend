using SharedKernels.Domain;

namespace AuditAggregatorService.Domain.Entities;

/// <summary>
/// GH-728 — job replay bền vững.
///
/// <para>Trước đây <c>POST /api/admin/audit/replay</c> trả 202 mà không lưu gì cả: không có
/// cách nào biết ai đã yêu cầu replay, có chạy không, chạy tới đâu. Entity này là thứ khiến
/// 202 trở thành lời hứa có thật — "đã ghi nhận" nghĩa là đã có một dòng trong bảng.</para>
/// </summary>
public class AuditReplayJob : AuditableEntity
{
    /// <summary>Service cần replay; <c>null</c> = tất cả (xem <c>AuditServiceNames</c>).</summary>
    public string? ServiceName { get; set; }

    /// <summary>Mốc đầu khoảng replay theo <c>OccurredAt</c> (UTC). Null = không giới hạn.</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>Mốc cuối khoảng replay theo <c>OccurredAt</c> (UTC). Null = không giới hạn.</summary>
    public DateTime? ToUtc { get; set; }

    public AuditReplayJobStatus Status { get; set; } = AuditReplayJobStatus.Requested;

    /// <summary>Số service phải phản hồi (1 nếu chỉ định service, 6 nếu replay tất cả).</summary>
    public int ExpectedResponders { get; set; }

    /// <summary>Số service đã phản hồi.</summary>
    public int RespondedCount { get; set; }

    /// <summary>Tổng số bản ghi audit đã được các service re-publish.</summary>
    public int RepublishedCount { get; set; }

    /// <summary>
    /// True nếu có service chạm trần an toàn và chưa replay hết khoảng yêu cầu.
    /// Job như vậy KHÔNG được coi là hoàn tất trọn vẹn.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>Tên các service đã phản hồi, phân tách bằng dấu phẩy — để chẩn đoán khi job treo.</summary>
    public string RespondedServices { get; set; } = string.Empty;

    /// <summary>Lỗi gộp từ các service báo thất bại (null nếu mọi service đều OK).</summary>
    public string? Error { get; set; }

    /// <summary>Account admin đã bấm replay (từ JWT).</summary>
    public Guid? RequestedByAccountId { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>Trạng thái job replay. Bắt đầu từ 1 theo quy ước enum của dự án.</summary>
public enum AuditReplayJobStatus
{
    /// <summary>Đã ghi nhận + đã publish yêu cầu, chưa service nào phản hồi.</summary>
    Requested = 1,

    /// <summary>Một phần service đã phản hồi.</summary>
    InProgress = 2,

    /// <summary>Đủ service phản hồi, tất cả thành công và không bị cắt ngắn.</summary>
    Completed = 3,

    /// <summary>Đủ service phản hồi nhưng có lỗi hoặc bị cắt ngắn — dữ liệu có thể chưa đầy đủ.</summary>
    CompletedWithErrors = 4,
}
