using AuthService.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.Interfaces.Helpers;

/// <summary>
/// #50 QA solars.io.vn 2026-08-29 — hệ thống KHÔNG có chốt chặn "Admin cuối cùng":
/// <c>MergeAccountCommandHandler</c>, <c>DeleteAccountCommandHandler</c>, và
/// <c>ChangeAccountRoleCommandHandler</c> đều không kiểm vai trò trước khi xoá/gộp/đổi role một
/// account — gộp/xoá/đổi role Admin duy nhất còn lại là khoá cửa vĩnh viễn (không ai còn quyền quản
/// trị để tự cứu). "Change role" nặng nhất: bấm phát ăn ngay, không hộp thoại, không lý do.
/// </summary>
public static class LastAdminGuard
{
    /// <summary>
    /// True nếu <paramref name="accountId"/> hiện là Admin (<paramref name="currentRoleId"/> khớp
    /// role Admin) VÀ không còn account Admin nào khác (chưa xoá) ngoài chính nó — tức thao tác xoá/
    /// gộp/đổi role account này sẽ làm hệ thống mất hết Admin.
    /// </summary>
    public static async Task<bool> WouldRemoveLastAdminAsync(
        IAuthUnitOfWork unitOfWork,
        Guid accountId,
        Guid? currentRoleId,
        CancellationToken cancellationToken = default)
    {
        if (!currentRoleId.HasValue)
            return false;

        var adminRoleId = await SystemRoleResolver.ResolveRoleIdAsync(unitOfWork, SystemRoleResolver.AdminNormalizedName, cancellationToken);
        if (adminRoleId is null || currentRoleId.Value != adminRoleId.Value)
            return false;

        var otherAdminCount = await unitOfWork.Accounts.GetAllAsync()
            .CountAsync(a => a.RoleId == adminRoleId && !a.IsDeleted && a.Id != accountId, cancellationToken);

        return otherAdminCount == 0;
    }
}
