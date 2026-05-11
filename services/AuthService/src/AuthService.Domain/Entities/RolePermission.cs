using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Bảng nối many-to-many giữa Role và Permission.
/// </summary>
public class RolePermission : AuditableEntity
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public Guid? AssignedBy { get; set; }

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
