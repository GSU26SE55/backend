using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Ghi nhận hành động nhạy cảm trong hệ thống Auth: login (thành/bại), đổi mật khẩu,
/// admin lock/unlock account, gán/thu hồi role, force logout, ...
/// Append-only — KHÔNG update / delete sau khi đã ghi.
/// </summary>
public class AuditLog : AuditableEntity
{
    /// <summary>Loại hành động (xem AuditActionEnum).</summary>
    public AuditActionEnum Action { get; set; }

    /// <summary>
    /// AccountId mục tiêu của hành động. Có thể null nếu chưa xác định được (ví dụ login với email không tồn tại).
    /// </summary>
    public Guid? TargetAccountId { get; set; }

    /// <summary>Email mục tiêu (khi action không cần TargetAccountId, ví dụ login fail với email lạ).</summary>
    public string? TargetEmail { get; set; }

    /// <summary>
    /// AccountId của actor thực hiện. Null = anonymous (login, register), bằng TargetAccountId = self,
    /// khác TargetAccountId = admin thực hiện.
    /// </summary>
    public Guid? ActorAccountId { get; set; }

    /// <summary>true nếu hành động thành công, false nếu thất bại / bị chặn.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Lý do thất bại hoặc note bổ sung (giới hạn 500 ký tự).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Metadata JSON tự do (ví dụ: { "oldStatus":1, "newStatus":2, "roleId":"..." }).
    /// Dùng cho thông tin chi tiết không cần index search.
    /// </summary>
    public string? MetadataJson { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>Correlation id của request (để link với request log / trace).</summary>
    public string? CorrelationId { get; set; }
}
