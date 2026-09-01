using TicketService.Application.Common.Models;

namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// GH-verify-sensor-grpc — đọc snapshot sensor pin (gRPC nội bộ tới BatteryService, KHÔNG JWT).
/// Dùng cho verify: AI đối chiếu mô tả ticket với sensor thật.
/// Fail/không có reading → trả null (verify vẫn chạy bằng heuristic text, KHÔNG chặn).
/// </summary>
public interface IBatterySensorClient
{
    /// <param name="assetId"></param>
    /// <param name="detectedAt">
    /// Thời điểm sự cố được khai báo. Null → lấy số đo mới nhất (hành vi cũ).
    /// Truyền mốc này vào thì snapshot phản ánh tình trạng pin LÚC XẢY RA sự cố: Customer
    /// thường mở app hàng giờ sau khi thấy vấn đề, đọc realtime khi đó thì pin đã nguội và AI
    /// trừ điểm một báo cáo đúng vì "sensor không thấy bất thường".
    /// </param>
    /// <param name="ct"></param>
    Task<TicketSensorSnapshotDto?> GetSnapshotAsync(
        Guid assetId, DateTime? detectedAt, CancellationToken ct);
}
