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
    /// <summary>Cửa sổ debounce theo AlertId — tránh spam cùng 1 alert (business logic).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Cửa sổ debounce theo MessageId — tránh duplicate khi MassTransit retry (idempotency).</summary>
    public static readonly TimeSpan MessageWindow = TimeSpan.FromMinutes(30);

    private static string Key(Guid alertId) => $"notif_debounce:{alertId}";
    private static string MessageKey(Guid messageId) => $"notif_msg:{messageId}";

    /// <summary>
    /// Trả <c>true</c> nếu đây là lần đầu trong cửa sổ (→ tiếp tục gửi); <c>false</c> nếu đã gửi
    /// trong 5 phút gần đây (→ caller nên skip). Dùng cho AlertId (business debounce).
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

    /// <summary>
    /// Trả <c>true</c> nếu message chưa được xử lý (→ tiếp tục); <c>false</c> nếu đã xử lý trong
    /// 30 phút gần đây (→ MassTransit retry — caller nên skip). Key dùng MessageId từ ConsumeContext.
    /// </summary>
    public static async Task<bool> TryBeginByMessageAsync(ICacheService cache, Guid messageId, CancellationToken cancellationToken)
    {
        var key = MessageKey(messageId);

        var existing = await cache.GetAsync<string>(key, cancellationToken);
        if (!string.IsNullOrEmpty(existing))
            return false;

        await cache.SetAsync(key, "1", MessageWindow, cancellationToken);
        return true;
    }
}
