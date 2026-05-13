using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Granular permission cho RBAC. Code dạng "module.action" (lowercase, dot-separated),
/// ví dụ: "battery.view", "ticket.assign", "user.delete".
///
/// Permission được gán cho Role qua <see cref="RolePermission"/>. Khi issue JWT,
/// hệ thống collect tất cả permission từ các role active của user và add vào claim "perm".
/// </summary>
public class Permission : AuditableEntity
{
    /// <summary>Unique code, format "module.action" (vd: "battery.view").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Module thuộc về (vd: "Battery", "Ticket", "User"). Dùng để filter/group trong UI admin.</summary>
    public string Module { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>true = permission do hệ thống tạo (không cho admin xóa).</summary>
    public bool IsSystemPermission { get; set; } = false;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
