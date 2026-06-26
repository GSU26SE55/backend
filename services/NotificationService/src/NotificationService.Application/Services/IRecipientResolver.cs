namespace NotificationService.Application.Services;

/// <summary>
/// GH-604 — Resolve recipient thật cho notification từ read-model account
/// (<see cref="NotificationService.Domain.Entities.AccountReadModel"/>).
/// Hiện chỉ resolve theo role (broadcast Manager/Admin) — xem quyết định kiến trúc trong issue #604.
/// </summary>
public interface IRecipientResolver
{
    /// <summary>
    /// Trả về danh sách AccountId của các account đang active có role khớp <paramref name="roles"/>
    /// (so khớp không phân biệt hoa-thường). Rỗng nếu không có account nào — caller phải skip + log,
    /// KHÔNG ghi notification với recipient rỗng.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveByRoleAsync(CancellationToken cancellationToken, params string[] roles);
}
