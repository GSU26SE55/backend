namespace TicketService.Domain.Enums;

public enum VirusScanStatusEnum
{
    /// <summary>Đã ghi nhận, chờ tới lượt quét.</summary>
    Pending = 1,

    /// <summary>Quét xong, sạch — tải xuống được.</summary>
    Clean = 2,

    /// <summary>Phát hiện mã độc — chặn tải.</summary>
    Infected = 3,

    /// <summary>
    /// Hỏng hẳn: đã thử đủ số lần cho phép mà vẫn không quét được.
    /// </summary>
    /// <remarks>
    /// GH-790 — trước đây MỘT lần hỏng là vào thẳng đây, mà worker chỉ quét bản ghi
    /// <see cref="Pending"/> nên không bao giờ thử lại. Một sự cố thoáng qua (FileStorage khởi động
    /// lại, ClamAV nghẽn) đủ để đính kèm không tải được vĩnh viễn.
    /// </remarks>
    Failed = 4,

    /// <summary>
    /// GH-790 — đã CHIẾM để quét, chưa biết kết quả.
    /// </summary>
    /// <remarks>
    /// Ghi trước khi tải file về, nên nhiều replica không cùng quét một đính kèm, và sau sự cố bản
    /// ghi nằm ở đây chứ không rơi lại hàng đợi. Bản ghi kẹt quá lâu được thu hồi về
    /// <see cref="Pending"/> — xem <c>VirusScanWorker</c>.
    /// </remarks>
    Scanning = 5
}
