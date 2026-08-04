using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Sprint 6.4 NOTI4-04 — 4 nhóm hệ thống theo vai trò.
///
/// <para><b>Vì sao phải seed thay vì để admin tự tạo:</b> 13 consumer sự kiện (SLA breach, ticket
/// mới, cảnh báo pin, thiết bị mất kết nối…) đang gửi cho "toàn bộ Manager" / "toàn bộ Admin" qua
/// <c>RecipientResolver</c>. Sau NOTI4-05 bộ phân giải đó đọc qua nhóm, nên 4 nhóm này phải tồn tại
/// từ lần khởi động đầu tiên — thiếu một cái là mọi thông báo tự động cho vai trò đó im lặng biến
/// mất, đúng kiểu lỗi mà NOTI4-00 vừa phải đi sửa.</para>
///
/// <para>Đánh dấu <c>IsSystem</c> nên không sửa/xoá được qua API.</para>
///
/// <para><b>Idempotent theo <c>RoleFilter</c></b> — chạy lại bao nhiêu lần cũng không đẻ thêm.
/// Cố ý KHÔNG ghi đè tên/mô tả: người vận hành có thể đã đổi tên hiển thị cho hợp ngữ cảnh
/// (thực ra API chặn sửa nhóm hệ thống, nhưng ai đó có thể sửa thẳng DB) — seeder không được xoá
/// công sức đó mỗi lần khởi động.</para>
/// </summary>
public class NotificationGroupSeeder
{
    /// <summary>
    /// Tên role phải khớp giá trị trong <c>account_read_models.role</c>, vốn đồng bộ từ
    /// <c>roles.name</c> bên AuthService: <c>Admin</c> · <c>Manager</c> · <c>Staff</c> ·
    /// <c>Customer</c>. So khớp lúc gửi không phân biệt hoa-thường nên lệch hoa-thường không sao,
    /// nhưng lệch chính tả thì nhóm sẽ rỗng vĩnh viễn mà không có gì báo lỗi.
    /// </summary>
    private static readonly (string RoleFilter, string Name, string Description)[] SystemGroups =
    {
        ("Admin", "Toàn bộ Quản trị viên",
            "Nhóm hệ thống — mọi tài khoản Quản trị viên đang hoạt động. Thành viên tự cập nhật theo vai trò."),
        ("Manager", "Toàn bộ Quản lý",
            "Nhóm hệ thống — mọi tài khoản Quản lý đang hoạt động. Thành viên tự cập nhật theo vai trò."),
        ("Staff", "Toàn bộ Nhân viên kỹ thuật",
            "Nhóm hệ thống — mọi tài khoản Nhân viên kỹ thuật đang hoạt động. Thành viên tự cập nhật theo vai trò."),
        ("Customer", "Toàn bộ Khách hàng",
            "Nhóm hệ thống — mọi tài khoản Khách hàng đang hoạt động. Thành viên tự cập nhật theo vai trò."),
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationGroupSeeder>? _logger;

    public NotificationGroupSeeder(ApplicationDbContext dbContext, ILogger<NotificationGroupSeeder>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Đọc CẢ dòng đã xoá mềm: partial unique index ux_notification_groups_role_filter lọc
        // is_deleted, nên một nhóm role đã xoá mềm không chặn INSERT — nhưng tạo bản thứ hai rồi để
        // bản cũ nằm đó là rác. Ai đó lỡ xoá thẳng DB thì hồi sinh bản cũ, giữ nguyên Id.
        var existing = await _dbContext.NotificationGroups
            .Where(g => g.Kind == NotificationGroupKindEnum.Role && g.RoleFilter != null)
            .ToListAsync(cancellationToken);

        var byRole = existing.ToDictionary(g => g.RoleFilter!.ToLowerInvariant());

        var added = 0;
        var revived = 0;

        foreach (var (roleFilter, name, description) in SystemGroups)
        {
            if (byRole.TryGetValue(roleFilter.ToLowerInvariant(), out var current))
            {
                if (!current.IsDeleted)
                    continue;

                current.IsDeleted = false;
                current.DeletedAt = null;
                current.IsSystem = true;
                revived++;
                continue;
            }

            _dbContext.NotificationGroups.Add(new NotificationGroup
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                Description = description,
                Kind = NotificationGroupKindEnum.Role,
                RoleFilter = roleFilter,
                IsSystem = true,
                CreatedAt = DateTime.UtcNow,
            });
            added++;
        }

        if (added == 0 && revived == 0)
            return;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Seeded nhóm hệ thống theo vai trò: thêm {Added}, hồi sinh {Revived}.", added, revived);
    }
}
