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
    IAsyncEnumerable<SseMessage> SubscribeAsync(TelemetryScope scope, CancellationToken cancellationToken);
}
