using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.Application.Interfaces.Repositories;

public interface INotificationUnitOfWork : IUnitOfWork
{
    IGenericRepository<Notification> Notifications { get; }
    IGenericRepository<DeviceToken> DeviceTokens { get; }
    IGenericRepository<NotificationAuditLog> NotificationAuditLogs { get; }       // Sprint audit #AUDIT-34
    IGenericRepository<NotificationAuditOutbox> NotificationAuditOutboxes { get; } // Sprint audit #AUDIT-34
    IGenericRepository<NotificationPreference> NotificationPreferences { get; }
    IGenericRepository<NotificationTemplate> NotificationTemplates { get; }
    IGenericRepository<AccountReadModel> Accounts { get; }

    /// <summary>Sprint 6.3 NOTI3-02 (#702) — biên nhận push để đối soát với Expo.</summary>
    IGenericRepository<PushReceipt> PushReceipts { get; }

    /// <summary>Sprint 6.3 NOTI3-04 (#704) — tuỳ chọn theo nhóm × kênh.</summary>
    IGenericRepository<NotificationCategoryPreference> NotificationCategoryPreferences { get; }

    /// <summary>Sprint 6.4 NOTI4-01 — nhóm người nhận để gửi hàng loạt.</summary>
    IGenericRepository<NotificationGroup> NotificationGroups { get; }

    /// <summary>Sprint 6.4 NOTI4-01 — bảng nối nhiều-nhiều người ↔ nhóm.</summary>
    IGenericRepository<NotificationGroupMember> NotificationGroupMembers { get; }

    /// <summary>Sprint 6.4 NOTI4-06 — nội dung một lần gửi, lưu đúng một lần.</summary>
    IGenericRepository<NotificationBatch> NotificationBatches { get; }

    /// <summary>Sprint 6.4 NOTI4-06 — bảng nối nhiều-nhiều lần gửi ↔ nhóm.</summary>
    IGenericRepository<NotificationBatchTarget> NotificationBatchTargets { get; }

    /// <summary>
    /// GH-793 — chiếm một bản ghi để gửi, bằng MỘT câu lệnh nguyên tử.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Đọc bản ghi rồi mới ghi <c>Processing</c> là hai bước tách rời: hai replica cùng đọc thấy
    /// <c>Pending</c> thì cả hai đều ghi được, và cả hai cùng gửi. Quyền chạy độc quyền (leader
    /// lease) thu hẹp khả năng đó nhưng không loại bỏ được — lease hết hạn giữa chừng, Redis sự cố,
    /// hoặc đồng hồ lệch là lại có hai chủ.
    /// </para>
    /// <para>
    /// Ở đây điều kiện <c>Status == Pending</c> nằm NGAY TRONG câu <c>UPDATE</c>, nên cơ sở dữ liệu
    /// là trọng tài: đúng một bên nhận được 1 dòng bị ảnh hưởng, bên kia nhận 0 và bỏ qua. Đây là
    /// hàng rào cuối cùng, đúng kể cả khi mọi tầng phía trên đều hỏng.
    /// </para>
    /// <para>
    /// Số lần thử tăng ngay tại đây, cùng câu lệnh: một lần chiếm việc là một lần thử, kể cả khi
    /// tiến trình chết ngay sau đó.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> nếu lời gọi này giành được bản ghi.</returns>
    Task<bool> TryClaimForDispatchAsync(Guid notificationId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>ADR-0019 — cấu hình cấp hệ thống sửa được lúc chạy (đường vận chuyển push).</summary>
    IGenericRepository<NotificationSetting> NotificationSettings { get; }
}
