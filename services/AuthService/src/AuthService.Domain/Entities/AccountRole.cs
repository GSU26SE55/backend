using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

/// <summary>
/// Bảng nối many-to-many giữa Account và Role.
/// Cho phép một account có nhiều role và ngược lại, kèm metadata (ai gán, khi nào, expire).
/// </summary>
public class AccountRole : AuditableEntity
{
    public Guid AccountId { get; set; }

    public Guid RoleId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public Guid? AssignedBy { get; set; }

    public DateTime? ExpiredAt { get; set; }

    public bool IsActive { get; set; } = true;

    public Account Account { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
