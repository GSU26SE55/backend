namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-11 (#711) — dọn notification cũ (section <c>Notification:Retention</c>).
///
/// **Vì sao cần:** bảng <c>notifications</c> chỉ tăng, không bao giờ giảm. Với 4 kênh × mỗi sự kiện,
/// một hệ thống chạy vài tháng đã có hàng triệu dòng, làm chậm chính truy vấn feed mà người dùng
/// nhìn thấy — và không ai đọc lại notification từ nửa năm trước.
/// </summary>
public class NotificationRetentionOptions
{
    public const string SectionName = "Notification:Retention";

    /// <summary>Bật/tắt worker dọn dẹp.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Giữ notification trong bấy nhiêu ngày kể từ <c>CreatedAt</c>.</summary>
    public int Days { get; set; } = 90;

    /// <summary>
    /// Số dòng xoá mềm mỗi vòng. Giới hạn để một lần dọn không khoá bảng lâu và không đẩy
    /// một khối WAL khổng lồ sang replica.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Số vòng tối đa mỗi lần chạy — chặn worker quét vô hạn khi tồn đọng lớn.</summary>
    public int MaxBatchesPerRun { get; set; } = 20;

    /// <summary>Giờ UTC chạy hằng đêm. Mặc định 18h UTC = 01h sáng giờ Việt Nam (giờ thấp điểm).</summary>
    public int RunAtUtcHour { get; set; } = 18;

    /// <summary>
    /// Giữ VĨNH VIỄN notification thuộc <c>CriticalTypes</c> (dùng chung danh sách với dispatcher).
    /// Đây là bằng chứng đã cảnh báo — cần cho điều tra sự cố và đối chiếu SLA, không được xoá theo hạn.
    /// </summary>
    public bool KeepCriticalForever { get; set; } = true;
}
