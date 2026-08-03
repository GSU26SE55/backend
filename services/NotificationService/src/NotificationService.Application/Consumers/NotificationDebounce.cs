using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-593 — Debounce notification theo AlertId (overall.md §49.2): trong 1 cửa sổ 5 phút,
/// chỉ xử lý event đầu tiên cho mỗi AlertId; các event trùng AlertId đến sau bị bỏ qua.
///
/// Sprint 6.3 NOTI3-09 (#709) — ĐÃ CHUYỂN SANG ATOMIC.
/// Bản cũ dùng <c>GetAsync</c> rồi <c>SetAsync</c>: hai lời gọi tách rời tạo cửa sổ tranh chấp,
/// 2 event trùng đến gần như đồng thời cùng đọc thấy "chưa có" nên cùng được xử lý (chính comment
/// cũ trong file này đã thừa nhận). Nay dùng <see cref="ICacheService.TrySetIfNotExistsAsync"/>
/// = <c>SET key val NX EX ttl</c>, một lệnh Redis duy nhất.
///
/// Vì sao phải sửa TRƯỚC NOTI3-08: bật retry ở tầng bus trên consumer chưa idempotent thật sự sẽ
/// nhân bản notification thay vì chỉ gửi lại (rủi ro R-41).
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
    /// Chiếm key bằng một lệnh atomic nên hai event song song chỉ đúng một bên nhận <c>true</c>.
    /// </summary>
    public static Task<bool> TryBeginAsync(ICacheService cache, Guid alertId, CancellationToken cancellationToken)
        => cache.TrySetIfNotExistsAsync(
            Key(alertId), DateTime.UtcNow.ToString("O"), Window, cancellationToken);

    /// <summary>
    /// Trả <c>true</c> nếu message chưa được xử lý (→ tiếp tục); <c>false</c> nếu đã xử lý trong
    /// 30 phút gần đây (→ MassTransit retry — caller nên skip). Key dùng MessageId từ ConsumeContext.
    /// </summary>
    public static Task<bool> TryBeginByMessageAsync(ICacheService cache, Guid messageId, CancellationToken cancellationToken)
        => cache.TrySetIfNotExistsAsync(
            MessageKey(messageId), DateTime.UtcNow.ToString("O"), MessageWindow, cancellationToken);
}
