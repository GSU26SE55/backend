using BatteryService.Application.Common.Models;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// BE-AI — đọc trạng thái sẵn sàng của AI module, transport-neutral.
/// Impl thật là <c>FallbackAiHealthClient</c> (gRPC primary → HTTP fallback, có cache).
/// </summary>
public interface IAiHealthClient
{
    /// <summary>
    /// Lấy trạng thái AI. Kết quả được CACHE trong <c>FallbackAiHealthClient</c> vì
    /// health gần như bất biến giữa hai lần deploy — gọi mỗi tick chỉ tốn round-trip.
    /// </summary>
    /// <returns>
    /// <see cref="AiHealthResult"/> nếu gọi được; <c>null</c> khi cả gRPC lẫn HTTP đều fail.
    /// Caller PHẢI xử lý <c>null</c> như "chưa biết", KHÔNG được coi là "AI hỏng" —
    /// job vẫn phải chạy tiếp bằng đường suy luận an toàn.
    /// </returns>
    Task<AiHealthResult?> GetHealthAsync(CancellationToken cancellationToken = default);
}
