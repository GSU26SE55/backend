using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Một mốc bảo trì định kỳ của một cục pin — nhật ký vòng đời của TÀI SẢN.
/// </summary>
/// <remarks>
/// <para>
/// Khác <c>maintenance_logs</c> bên TicketService: log đó là báo cáo công việc Staff ghi
/// trong lúc xử lý một ticket (chẩn đoán gì, làm gì, mất bao lâu). Bản ghi này trả lời câu
/// hỏi ở tầng tài sản: kỳ này rơi vào lúc nào, và sức khoẻ pin tại thời điểm đó ra sao.
/// </para>
/// <para>
/// <see cref="SohPercentAtCycle"/> là lý do bảng này tồn tại: SoH hiện tại xem realtime là
/// biết, nhưng SoH TẠI TỪNG MỐC thì không tái tạo lại được. Đặt các kỳ cạnh nhau mới thấy
/// được đường suy giảm qua từng chu kỳ.
/// </para>
/// </remarks>
public class MaintenanceCycle : AuditableEntity
{
    public Guid BatteryAssetId { get; set; }

    /// <summary>Số thứ tự kỳ — 1 là kỳ đầu tiên kể từ khi lắp đặt.</summary>
    public int CycleNo { get; set; }

    /// <summary>Hạn theo kế hoạch của kỳ này.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>Thời điểm hệ thống thực sự ghi mốc (có thể trễ vài phút so với hạn).</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>SoH (%) tại mốc này — dùng để so sánh sức khoẻ giữa các kỳ.</summary>
    public decimal? SohPercentAtCycle { get; set; }

    /// <summary>
    /// Ticket bảo trì mở cho kỳ này, hoặc <c>null</c> khi chưa nhận được phản hồi.
    /// </summary>
    /// <remarks>
    /// Điền BẤT ĐỒNG BỘ, không phải lúc ghi mốc: dòng này được ghi trước, rồi
    /// <c>MaintenanceCycleDueEvent</c> mới bay sang TicketService — lúc INSERT thì ticket
    /// chưa tồn tại. TicketService tạo ticket xong phát ngược
    /// <c>PeriodicMaintenanceTicketRaisedEvent</c> và consumer bên này mới cập nhật cột.
    ///
    /// Vì vậy <c>null</c> có ba nghĩa khác nhau, đừng coi là "hỏng": kỳ vừa ghi và event
    /// chưa về; kỳ ghi từ trước khi có cột này (chưa backfill); hoặc TicketService đã bỏ
    /// qua vì ticket cho kỳ đó đã tồn tại.
    ///
    /// Cố ý KHÔNG đặt khoá ngoại: ticket nằm ở service khác, ràng buộc chéo database sẽ
    /// khoá hai service vào nhau.
    /// </remarks>
    public Guid? TicketId { get; set; }

    // ── Ảnh chụp tình trạng pin trong kỳ vừa qua ─────────────────────────────
    //
    // Tổng hợp từ sensor_readings và alerts trong khoảng [kỳ trước, kỳ này], chụp một lần
    // lúc worker ghi mốc. Chụp thay vì tính lại khi đọc vì hai lý do: sensor_readings là
    // hypertable có chính sách lưu trữ (dữ liệu cũ sẽ bị dọn), và tính gộp 6 tháng mỗi lần
    // mở trang là quá đắt.
    //
    // Tất cả đều nullable: pin mất kết nối cả kỳ thì không có gì để tổng hợp, và điều đó
    // không được phép chặn việc ghi mốc.

    /// <summary>Nhiệt độ trung bình (°C) trong kỳ.</summary>
    public decimal? AvgTemperatureCelsius { get; set; }

    /// <summary>Nhiệt độ cao nhất (°C) ghi nhận trong kỳ.</summary>
    public decimal? MaxTemperatureCelsius { get; set; }

    /// <summary>Điện áp thấp nhất (V) trong kỳ.</summary>
    public decimal? MinVoltage { get; set; }

    /// <summary>Điện áp cao nhất (V) trong kỳ.</summary>
    public decimal? MaxVoltage { get; set; }

    /// <summary>Số chu kỳ sạc/xả tăng thêm trong kỳ.</summary>
    public int? CycleCountDelta { get; set; }

    /// <summary>Số cảnh báo phát sinh trong kỳ.</summary>
    public int? AlertCount { get; set; }

    /// <summary>Số cảnh báo mức Critical trong kỳ.</summary>
    public int? CriticalAlertCount { get; set; }

    /// <summary>Số bản ghi cảm biến dùng để tổng hợp — 0 nghĩa là pin mất kết nối cả kỳ.</summary>
    public int? ReadingCount { get; set; }

    public BatteryAsset BatteryAsset { get; set; } = null!;
}
