using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Vai trò người dùng trong hệ thống (Admin, Manager, Technician, Customer...).
/// </summary>
public class Role : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string NormalizedName { get; set; } = null!;

    public string? Description { get; set; }

    public RoleStatusEnum Status { get; set; } = RoleStatusEnum.Active;

    public bool IsSystemRole { get; set; } = false;

    public ICollection<AccountRole> AccountRoles { get; set; } = new List<AccountRole>();

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
