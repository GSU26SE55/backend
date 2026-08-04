using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.4 — nơi DUY NHẤT định nghĩa "ai là người nhận của một nhóm".
///
/// <para>Quy tắc này được dùng ở bốn chỗ rời nhau: đếm thành viên trên danh sách nhóm, đếm trên chi
/// tiết nhóm, liệt kê thành viên, và nở người nhận lúc gửi hàng loạt. Mỗi chỗ tự viết lại điều kiện
/// là kiểu gì cũng có chỗ lệch — mà lệch ở đây thì hoặc admin thấy "12 người" rồi gửi đi chỉ 9 người
/// nhận, hoặc người đã nghỉ vẫn nhận được thông báo nội bộ. Cả hai đều im lặng, không có gì báo lỗi.</para>
///
/// <para><b>Hai luật bất biến:</b>
/// <list type="number">
/// <item>Người nhận phải <c>IsActive</c> và chưa xoá trong read-model tài khoản — thành viên trỏ tới
/// tài khoản đã nghỉ vẫn nằm trong bảng nhưng không bao giờ được tính.</item>
/// <item>Nhóm <see cref="NotificationGroupKindEnum.Role"/> KHÔNG đọc bảng thành viên; người nhận suy
/// ra từ read-model theo <c>RoleFilter</c>, so khớp không phân biệt hoa-thường.</item>
/// </list></para>
/// </summary>
public static class NotificationGroupMembership
{
    /// <summary>
    /// Tài khoản đủ điều kiện nhận thông báo. Lọc <c>!IsDeleted</c> tường minh vì dự án KHÔNG dùng
    /// global query filter.
    /// </summary>
    public static IQueryable<AccountReadModel> NotifiableAccounts(INotificationUnitOfWork unitOfWork)
        => unitOfWork.Accounts.GetAllAsync(false).Where(a => !a.IsDeleted && a.IsActive);

    /// <summary>Id người nhận của một nhóm — dùng chung cho đếm, liệt kê và gửi.</summary>
    public static IQueryable<Guid> RecipientIds(INotificationUnitOfWork unitOfWork, NotificationGroup group)
    {
        var accounts = NotifiableAccounts(unitOfWork);

        if (group.Kind == NotificationGroupKindEnum.Role)
        {
            var role = (group.RoleFilter ?? string.Empty).ToLower();
            return accounts.Where(a => a.Role.ToLower() == role).Select(a => a.Id);
        }

        return unitOfWork.NotificationGroupMembers.GetAllAsync(false)
            .Where(m => !m.IsDeleted && m.GroupId == group.Id)
            .Join(accounts, m => m.UserId, a => a.Id, (m, a) => a.Id);
    }

    /// <summary>Số người nhận thực tế của một nhóm.</summary>
    public static Task<int> CountRecipientsAsync(
        INotificationUnitOfWork unitOfWork, NotificationGroup group, CancellationToken cancellationToken)
        => RecipientIds(unitOfWork, group).CountAsync(cancellationToken);

    /// <summary>
    /// Đủ thông tin để đếm người nhận của một nhóm mà không cần kéo cả entity — để handler danh sách
    /// chiếu thẳng sang DTO trong SQL thay vì phải chiếu qua entity (EF chiếu sang entity trong
    /// <c>Select</c> là chuyện dễ vỡ, nhất là khi đổi tracking).
    /// </summary>
    public readonly record struct GroupKey(Guid Id, NotificationGroupKindEnum Kind, string? RoleFilter);

    /// <summary>
    /// Đếm người nhận cho NHIỀU nhóm bằng 2 truy vấn gộp thay vì mỗi nhóm một truy vấn — dùng cho
    /// màn hình danh sách (một trang 10 nhóm sẽ là 20 lượt đi DB nếu đếm lẻ).
    /// </summary>
    /// <returns>Bảng tra <c>groupId → số người nhận</c>. Nhóm không có ai thì không có mặt.</returns>
    public static async Task<Dictionary<Guid, int>> CountRecipientsAsync(
        INotificationUnitOfWork unitOfWork,
        IReadOnlyCollection<GroupKey> groups,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, int>();
        if (groups.Count == 0)
            return result;

        var accounts = NotifiableAccounts(unitOfWork);

        var staticIds = groups
            .Where(g => g.Kind == NotificationGroupKindEnum.Static)
            .Select(g => g.Id)
            .Distinct()
            .ToList();

        if (staticIds.Count > 0)
        {
            var perGroup = await unitOfWork.NotificationGroupMembers.GetAllAsync(false)
                .Where(m => !m.IsDeleted && staticIds.Contains(m.GroupId))
                .Join(accounts, m => m.UserId, a => a.Id, (m, a) => m.GroupId)
                .GroupBy(groupId => groupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var row in perGroup)
                result[row.GroupId] = row.Count;
        }

        var roleGroups = groups
            .Where(g => g.Kind == NotificationGroupKindEnum.Role && !string.IsNullOrWhiteSpace(g.RoleFilter))
            .ToList();

        if (roleGroups.Count > 0)
        {
            var roles = roleGroups.Select(g => g.RoleFilter!.ToLower()).Distinct().ToList();

            var perRole = await accounts
                .Where(a => roles.Contains(a.Role.ToLower()))
                .GroupBy(a => a.Role.ToLower())
                .Select(g => new { Role = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var byRole = perRole.ToDictionary(r => r.Role, r => r.Count);

            foreach (var group in roleGroups)
            {
                if (byRole.TryGetValue(group.RoleFilter!.ToLower(), out var count))
                    result[group.Id] = count;
            }
        }

        return result;
    }
}
