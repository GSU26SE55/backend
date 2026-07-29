using TicketService.Application.Common.Models;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// GH-verify-sensor-grpc — đọc snapshot sensor pin (gRPC nội bộ tới BatteryService, KHÔNG JWT).
/// Dùng cho verify: AI đối chiếu mô tả ticket với sensor thật.
/// Fail/không có reading → trả null (verify vẫn chạy bằng heuristic text, KHÔNG chặn).
/// </summary>
public interface IBatterySensorClient
{
    Task<TicketSensorSnapshotDto?> GetSnapshotAsync(Guid assetId, CancellationToken ct);
}
