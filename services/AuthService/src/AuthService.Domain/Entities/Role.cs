using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Vai trò người dùng trong hệ thống (Admin, Manager, Staff, Customer).
/// Quan hệ 1-N: 1 Role có thể được gán cho nhiều Account, nhưng mỗi Account chỉ có 1 Role.
/// </summary>
public class Role : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string NormalizedName { get; set; } = null!;

    public string? Description { get; set; }

    public RoleStatusEnum Status { get; set; } = RoleStatusEnum.Active;

    public bool IsSystemRole { get; set; } = false;

    public ICollection<Account> Accounts { get; set; } = new List<Account>();

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
