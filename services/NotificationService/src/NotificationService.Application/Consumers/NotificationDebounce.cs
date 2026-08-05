using MassTransit;
using Microsoft.Extensions.Logging;
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
    /// GH-765 — hạn CHỖ GIỮ trong lúc consumer đang chạy (ngắn), tách khỏi
    /// <see cref="MessageWindow"/> là cửa sổ chống trùng sau khi đã ghi xong.
    /// </summary>
    /// <remarks>
    /// Phải dài hơn một lượt xử lý chậm nhất (resolve recipient + ghi DB), nhưng đủ ngắn để một
    /// tiến trình chết giữa chừng không khoá message lại tới 30 phút.
    /// </remarks>
    public static readonly TimeSpan MessageLease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// GH-765 — chạy <paramref name="action"/> đúng một lần cho mỗi <paramref name="messageId"/>,
    /// và chỉ đánh dấu "đã xử lý" SAU KHI nó thành công.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bản cũ (<c>TryBeginByMessageAsync</c>) chiếm key 30 phút NGAY TỪ ĐẦU — trước cả khi resolve
    /// recipient và trước khi ghi DB. Một lỗi DB/resolver ở lần đầu là mọi lần MassTransit gửi lại
    /// trong 30 phút đều thấy key, log "duplicate" rồi return. Notification biến mất hẳn dù broker
    /// đã làm đúng phần việc của nó.
    /// </para>
    /// <para>
    /// Nay: giữ chỗ có hạn ngắn → chạy → nếu xong thì nâng lên cửa sổ 30 phút, nếu lỗi thì nhả
    /// ngay để lần gửi lại chạy thật. Nhả có so dấu sở hữu nên một chỗ giữ đã hết hạn không xoá
    /// nhầm lượt xử lý của người khác.
    /// </para>
    /// <para>
    /// KHÔNG đụng tới debounce theo AlertId (<see cref="TryBeginAsync"/>) — đó là luật nghiệp vụ
    /// "một alert chỉ báo một lần trong 5 phút", cố ý bỏ qua các event sau, hoàn toàn khác việc
    /// chống trùng do retry.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> nếu action đã chạy; <c>false</c> nếu bỏ qua vì trùng.</returns>
    public static async Task<bool> ProcessOnceByMessageAsync(
        ICacheService cache, Guid messageId, CancellationToken cancellationToken, Func<Task> action)
    {
        var key = MessageKey(messageId);
        var token = Guid.NewGuid().ToString("N");

        if (!await cache.TrySetIfNotExistsAsync(key, token, MessageLease, cancellationToken))
            return false;

        try
        {
            await action();
        }
        catch
        {
            // Nhả chỗ giữ để lần gửi lại chạy THẬT. Bọc riêng để lỗi lúc nhả không che lỗi gốc —
            // lỗi gốc mới là thứ quyết định chính sách thử lại của MassTransit.
            try { await cache.TryReleaseLeaseAsync(key, token, cancellationToken); }
            catch { /* nhả hụt thì chỗ giữ tự hết hạn sau MessageLease */ }
            throw;
        }

        // Xong rồi mới nâng lên cửa sổ chống trùng dài. Lỗi ở bước này KHÔNG được ném: side effect
        // đã thành công, ném ra sẽ kéo theo một lần gửi lại và tạo notification thứ hai.
        try { await cache.TryRefreshLeaseAsync(key, token, MessageWindow, cancellationToken); }
        catch { /* chỗ giữ tự hết hạn; cùng lắm là xử lý lại, không mất dữ liệu */ }

        return true;
    }

    /// <summary>
    /// GH-765 — bản tiện dụng cho consumer: lấy MessageId từ context, ghi log khi bỏ qua vì trùng.
    /// </summary>
    /// <remarks>
    /// Message không có MessageId thì không dedupe được — vẫn phải làm việc, y như hành vi cũ.
    /// Bỏ qua nó sẽ mất notification vì một lý do còn vô lý hơn.
    /// </remarks>
    public static async Task ProcessOnceAsync<T>(
        ICacheService cache,
        ConsumeContext<T> context,
        string eventName,
        ILogger logger,
        Func<Task> action)
        where T : class
    {
        var messageId = context.MessageId ?? Guid.Empty;
        if (messageId == Guid.Empty)
        {
            await action();
            return;
        }

        var ran = await ProcessOnceByMessageAsync(cache, messageId, context.CancellationToken, action);
        if (!ran)
            logger.LogInformation("Debounce: skip duplicate {Event} message={MessageId}", eventName, messageId);
    }
}
