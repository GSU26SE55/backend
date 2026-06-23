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

    public DateTime LastSyncedAtUtc { get; set; }
}
