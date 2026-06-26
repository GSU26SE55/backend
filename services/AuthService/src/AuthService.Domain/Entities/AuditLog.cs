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

    // ===== Sprint audit #AUDIT-06 — 14 cột chuẩn Hybrid Audit (đồng bộ AuditCreatedEventV1) =====

    /// <summary>Idempotency key xuyên hệ thống (Guid v7 khi có, fallback v4). Unique.</summary>
    public Guid EventId { get; set; }

    /// <summary>Service phát sinh — luôn "AuthService".</summary>
    public string ServiceName { get; set; } = "AuthService";

    /// <summary>Action code chuẩn dạng string (map từ <see cref="Action"/> enum) — xem SharedContracts ActionCodes.</summary>
    public string ActionCode { get; set; } = string.Empty;

    /// <summary>Category chuẩn (9 fixed) — xem SharedContracts AuditCategories.</summary>
    public string ActionCategory { get; set; } = string.Empty;

    /// <summary>Severity chuẩn (Info/Warning/Critical/Security).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Loại target chuẩn (Account/Role/Session/...) — xem SharedContracts TargetTypes.</summary>
    public string? TargetType { get; set; }

    /// <summary>Id target chung (generic). Với Auth thường = <see cref="TargetAccountId"/>.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Tên hiển thị target (denormalized cho forensic), vd email/full name.</summary>
    public string? TargetDisplay { get; set; }

    /// <summary>Role của actor lúc hành động (Admin/Manager/Staff/Customer/System).</summary>
    public string? ActorRole { get; set; }

    /// <summary>Tên hiển thị actor (denormalized).</summary>
    public string? ActorDisplay { get; set; }

    /// <summary>Mã lỗi khi <see cref="IsSuccess"/>=false.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Event id của parent event (cross-service causation chain).</summary>
    public Guid? CausationId { get; set; }

    /// <summary>UTC — thời điểm hành động xảy ra (handler set).</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>UTC — thời điểm ghi DB (handler set).</summary>
    public DateTime RecordedAt { get; set; }
}
