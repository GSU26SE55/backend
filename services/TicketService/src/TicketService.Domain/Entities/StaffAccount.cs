using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class StaffAccount : AuditableEntity
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }

    /// <summary>Ảnh đại diện đồng bộ từ AuthService — dùng vẽ avatar "đã xem" trong chat.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Vai trò đồng bộ từ AuthService: "Staff" | "Manager" | "Admin".
    /// </summary>
    /// <remarks>
    /// Bảng này chứa CẢ BA vai trò — <c>AccountSyncConsumer</c> tạo <c>StaffAccount</c> cho
    /// Staff, Manager lẫn Admin (xem điều kiện <c>isStaff</c> ở đó). Trước đây giá trị vai trò
    /// bị bỏ đi sau khi lọc, nên không còn cách nào phân biệt: mọi truy vấn "danh sách kỹ thuật
    /// viên" đều kéo theo cả Manager/Admin.
    /// <para>
    /// Mặc định "Staff" cho dữ liệu cũ — đúng với đa số bản ghi; Manager/Admin sẽ được ghi đè
    /// ở lần đồng bộ kế tiếp.
    /// </para>
    /// </remarks>
    public string Role { get; set; } = "Staff";

    public AccountStatusEnum Status { get; set; }
    public bool IsAvailable { get; set; } = true;
    public int MaxConcurrentTickets { get; set; } = 10;
    public StaffSkillTierEnum SkillTier { get; set; } = StaffSkillTierEnum.Generalist;
    public List<string> SkillCodes { get; set; } = new();
    public DateTime LastSyncedAt { get; set; }

    /// <summary>Mốc event account (email/name/role/status/delete) mới nhất đã áp.</summary>
    public DateTime? LastSourceEventAtUtc { get; set; }

    /// <summary>Mốc event hồ sơ/skill Staff mới nhất đã áp, tách khỏi account để hai luồng không chặn nhau.</summary>
    public DateTime? LastStaffProfileSourceEventAtUtc { get; set; }
}
