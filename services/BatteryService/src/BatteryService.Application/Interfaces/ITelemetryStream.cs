using BatteryService.Application.Realtime;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint BE-IoT-Realtime (#614/#618) — nguồn stream SSE cho 1 kết nối.
/// Infrastructure subscribe Redis pub/sub, coalesce <c>summary</c> (customer/site) + heartbeat <c>ping</c>,
/// yield ra <see cref="SseMessage"/>. Api chỉ iterate + ghi xuống response (không phụ thuộc Redis).
/// </summary>
public interface ITelemetryStream
{
    /// <summary>
    /// Mở stream cho <paramref name="scope"/>. Hoàn tất khi <paramref name="cancellationToken"/> hủy
    /// (client disconnect / <c>RequestAborted</c>) — tự unsubscribe Redis.
    /// </summary>
    /// <param name="lastEventId">
    /// Sprint BE-IoT-Realtime <c>#614</c> — giá trị header <c>Last-Event-ID</c> client gửi lên khi
    /// reconnect. Có giá trị ⇒ phát bù các sự kiện SAU id đó trước, rồi mới nối luồng trực tiếp.
    /// <c>null</c> ⇒ kết nối mới, chỉ nhận từ hiện tại. Chỉ có tác dụng với scope 1 pin.
    /// </param>
    IAsyncEnumerable<SseMessage> SubscribeAsync(
        TelemetryScope scope, string? lastEventId, CancellationToken cancellationToken);
}
