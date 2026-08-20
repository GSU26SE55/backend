namespace BatteryService.Application.Import;

/// <summary>Tham số vận hành của luồng nhập dữ liệu bên thứ ba.</summary>
public class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>Số dòng tối đa cho một lô. Vượt thì từ chối cả file thay vì cắt bớt im lặng.</summary>
    public int MaxRowsPerBatch { get; set; } = 5000;

    /// <summary>
    /// Thời gian chờ bản sao khách hàng đồng bộ từ AuthService trước khi bỏ cuộc.
    /// </summary>
    /// <remarks>
    /// Có giới hạn này vì bản sao đi qua outbox rồi qua message bus: nếu một mắt xích tắc thì
    /// không có gì tự báo. Không đặt hạn thì lô nằm im mãi ở trạng thái chờ và người dùng chỉ biết
    /// là "nó không chạy", không biết vì sao.
    /// </remarks>
    public int AccountSyncTimeoutMinutes { get; set; } = 10;

    /// <summary>Nhịp quét lô đang chờ xử lý (giây).</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Số ngày giữ lô đã xong trước khi dọn.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Số năm tối đa cho phép lùi về quá khứ của ngày lắp đặt trong dữ liệu nhập.
    /// </summary>
    /// <remarks>
    /// Đường tạo thủ công chặn ở 5 năm, hợp lý cho việc nhập tay một thiết bị vừa lắp. Dữ liệu bàn
    /// giao từ đối tác thì khác hẳn: một đơn vị lắp đặt hoạt động lâu năm sẽ có pin lắp từ 2018,
    /// và với ngưỡng 5 năm thì gần như cả file bị loại.
    /// </remarks>
    public int MaxInstallDateAgeYears { get; set; } = 15;

    /// <summary>Số lần thử ghi lại một dòng trước khi đánh hỏng.</summary>
    public int MaxRowAttempts { get; set; } = 5;
}
