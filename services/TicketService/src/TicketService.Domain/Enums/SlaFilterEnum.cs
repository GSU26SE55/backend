namespace TicketService.Domain.Enums;

/// <summary>
/// Bộ lọc SLA cho danh sách ticket. KHÔNG phải trạng thái lưu trong DB —
/// <see cref="SlaTimerStatusEnum"/> mới là cột thật; enum này chỉ là 3 tình huống
/// Admin/Manager cần lọc ra để xử lý.
///
/// Cả ba đều nằm TRONG vòng đời đang xử lý: ticket vẫn giữ nguyên Status của nó
/// (thường là InProgress). Chỉ <see cref="Breached"/> mới là "đã về 0".
/// </summary>
public enum SlaFilterEnum
{
    /// <summary>Đồng hồ SLA đang tạm dừng (Staff hold / chờ khách).</summary>
    Paused = 1,

    /// <summary>Đã bắn cảnh báo sắp hết hạn nhưng CHƯA hết hạn — vẫn đang chạy.</summary>
    Warning = 2,

    /// <summary>Đã quá hạn: DueAt về 0, background job đóng dấu Breached.</summary>
    Breached = 3
}
