using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Services;

/// <summary>
/// GH-604 — Resolve recipient theo role từ read-model account. Query đọc-thuần
/// qua <see cref="INotificationUnitOfWork"/> (không SaveChanges). LUÔN filter
/// <c>!IsDeleted</c> (dự án không dùng global query filter).
///
/// <para>Sprint 6.4 NOTI4-05 — thêm <see cref="GetGroupRecipientsAsync"/>. Cả hai phương thức lấy
/// tập tài khoản đủ điều kiện từ <see cref="NotificationGroupMembership.NotifiableAccounts"/> để
/// không có chỗ nào lệch định nghĩa "ai được nhận".</para>
/// </summary>
public class RecipientResolver : IRecipientResolver
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public RecipientResolver(INotificationUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<Guid>> GetActiveByRoleAsync(
        CancellationToken cancellationToken, params string[] roles)
    {
        if (roles is null || roles.Length == 0)
            return Array.Empty<Guid>();

        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim().ToLower())
            .Distinct()
            .ToArray();

        if (normalized.Length == 0)
            return Array.Empty<Guid>();

        // Đọc THẲNG read-model, KHÔNG qua nhóm Role — xem lý do đầy đủ ở IRecipientResolver: đi qua
        // nhóm không làm việc định tuyến trở thành dữ liệu (nhóm gắn cứng đúng một role), nhưng lại
        // khiến mọi thông báo tự động phụ thuộc vào 4 dòng seed.
        return await NotificationGroupMembership.NotifiableAccounts(_unitOfWork)
            .Where(x => normalized.Contains(x.Role.ToLower()))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetGroupRecipientsAsync(
        IEnumerable<Guid> groupIds, CancellationToken cancellationToken)
    {
        var ids = groupIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0)
            return Array.Empty<Guid>();

        var groups = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .Where(g => !g.IsDeleted && ids.Contains(g.Id))
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
            return Array.Empty<Guid>();

        var accounts = NotificationGroupMembership.NotifiableAccounts(_unitOfWork);

        // Gộp thành HAI truy vấn (một cho nhóm tĩnh, một cho nhóm theo vai trò) thay vì mỗi nhóm một
        // truy vấn — nhắm 5 nhóm thì vẫn chỉ 2 lượt đi DB.
        var staticIds = groups
            .Where(g => g.Kind == NotificationGroupKindEnum.Static)
            .Select(g => g.Id)
            .ToList();

        var roleFilters = groups
            .Where(g => g.Kind == NotificationGroupKindEnum.Role && !string.IsNullOrWhiteSpace(g.RoleFilter))
            .Select(g => g.RoleFilter!.ToLower())
            .Distinct()
            .ToList();

        var recipients = new HashSet<Guid>();

        if (staticIds.Count > 0)
        {
            var fromMembers = await _unitOfWork.NotificationGroupMembers.GetAllAsync(false)
                .Where(m => !m.IsDeleted && staticIds.Contains(m.GroupId))
                .Join(accounts, m => m.UserId, a => a.Id, (m, a) => a.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var id in fromMembers)
                recipients.Add(id);
        }

        if (roleFilters.Count > 0)
        {
            var fromRoles = await accounts
                .Where(a => roleFilters.Contains(a.Role.ToLower()))
                .Select(a => a.Id)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var id in fromRoles)
                recipients.Add(id);
        }

        // HashSet đã gom trùng giữa các nhóm giao nhau — lớp chống nhận trùng thứ nhất.
        return recipients.ToList();
    }
}
