namespace NotificationService.Domain.Enums;

/// <summary>
/// Action audit của NotificationService — 7 action gốc (#AUDIT-34) + 1 bổ sung Sprint 6.3.
/// Enum bắt đầu từ 1.
/// </summary>
public enum NotificationAuditActionEnum
{
    PushSent = 1,
    PushFailed = 2,
    PushDelivered = 3,
    PushOpened = 4,
    InAppCreated = 5,
    InAppRead = 6,
    InAppDismissed = 7,

    /// <summary>Sprint 6.3 NOTI3-12 (#712) — admin gửi thử một template email.</summary>
    TemplateTestSent = 8,

    // 02/08/2026 — vòng đời template soạn từ giao diện quản trị.
    /// <summary>Tạo template đầu tiên cho một cặp (Type × Channel).</summary>
    TemplateCreated = 9,

    /// <summary>Sửa nội dung ⇒ sinh phiên bản mới và bật nó lên.</summary>
    TemplateRevised = 10,

    /// <summary>Quay lui: bật lại một phiên bản cũ.</summary>
    TemplateActivated = 11,

    /// <summary>Xoá mềm một phiên bản không còn dùng.</summary>
    TemplateDeleted = 12,

    // Sprint 6.4 NOTI4-02/03/07 — nhóm người nhận và gửi hàng loạt.
    /// <summary>Tạo một nhóm người nhận mới.</summary>
    GroupCreated = 13,

    /// <summary>Đổi tên / mô tả một nhóm.</summary>
    GroupUpdated = 14,

    /// <summary>Xoá mềm một nhóm cùng toàn bộ thành viên của nó.</summary>
    GroupDeleted = 15,

    /// <summary>Thêm một hoặc nhiều người vào nhóm.</summary>
    GroupMembersAdded = 16,

    /// <summary>Bỏ một người khỏi nhóm.</summary>
    GroupMemberRemoved = 17,

    /// <summary>Gửi thông báo hàng loạt cho một hoặc nhiều nhóm / cá nhân.</summary>
    BroadcastSent = 18,
}
