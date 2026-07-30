using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// GH-604 — Read-model account đồng bộ từ UserService qua message bus
/// (AccountActivated / AccountProfileUpdated / AccountDeleted). Dùng để resolve
/// recipient thật cho notification (vd broadcast toàn bộ Manager/Admin) thay cho
/// placeholder <see cref="System.Guid.Empty"/>.
///
/// <see cref="BaseEntity.Id"/> = AccountId bên AuthService (không tự sinh).
/// Sync best-effort: UserService chưa publish event đổi-role/deactivate nên Role/IsActive
/// có thể stale — chấp nhận ở scope capstone. Mirror pattern CustomerAccount của BatteryService.
/// </summary>
public class AccountReadModel : AuditableEntity
{
    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>Role hiện tại (single — quan hệ 1-N): "Admin" | "Manager" | "Staff" | "Customer".</summary>
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sprint 6.3 NOTI3-12 (#712) — locale BCP-47 ưa dùng của người nhận (<c>vi-VN</c>, <c>en-US</c>).
    ///
    /// <c>null</c> ⇒ dùng <c>Notification:Dispatch:DefaultLocale</c>. Trước sprint này dispatcher
    /// hardcode <c>vi-VN</c>, nên khách hàng không đọc tiếng Việt vẫn nhận thông báo tiếng Việt.
    /// UserService chưa publish trường này ⇒ hiện luôn <c>null</c> và rơi về mặc định; cột có sẵn để
    /// khi UserService bổ sung thì chỉ cần map, không phải đổi migration.
    /// </summary>
    public string? PreferredLocale { get; set; }

    public DateTime LastSyncedAtUtc { get; set; }
}
