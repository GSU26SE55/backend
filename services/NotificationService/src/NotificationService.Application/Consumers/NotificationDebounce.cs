using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-593 — Debounce notification theo AlertId (overall.md §49.2): trong 1 cửa sổ 5 phút,
/// chỉ xử lý event đầu tiên cho mỗi AlertId; các event trùng AlertId đến sau bị bỏ qua.
///
/// Dùng <see cref="ICacheService"/> (Redis). Lưu ý: Get-rồi-Set KHÔNG atomic — 2 event cùng
/// AlertId đến gần như đồng thời có thể cùng pass; chấp nhận ở volume capstone.
/// </summary>
internal static class NotificationDebounce
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private static string Key(Guid alertId) => $"notif_debounce:{alertId}";

    /// <summary>
    /// Trả <c>true</c> nếu đây là lần đầu trong cửa sổ (→ tiếp tục gửi); <c>false</c> nếu đã gửi
    /// trong 5 phút gần đây (→ caller nên skip).
    /// </summary>
    public static async Task<bool> TryBeginAsync(ICacheService cache, Guid alertId, CancellationToken cancellationToken)
    {
        var key = Key(alertId);

        var existing = await cache.GetAsync<string>(key, cancellationToken);
        if (!string.IsNullOrEmpty(existing))
            return false;

        await cache.SetAsync(key, DateTime.UtcNow.ToString("O"), Window, cancellationToken);
        return true;
    }
}
