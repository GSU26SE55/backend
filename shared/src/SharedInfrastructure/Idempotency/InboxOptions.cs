namespace SharedInfrastructure.Idempotency;

public class InboxOptions
{
    public const string SectionName = "Inbox";

    /// <summary>Thời gian giữ dấu "đã xử lý xong" — cửa sổ chống trùng thật sự.</summary>
    public int TtlDays { get; set; } = 7;

    /// <summary>
    /// GH-764 — hạn của CHỖ GIỮ trong lúc side effect đang chạy (giây).
    /// </summary>
    /// <remarks>
    /// Phải dài hơn side effect chậm nhất (gửi email/SMS qua nhà cung cấp ngoài, đồng bộ DB), kẻo
    /// chỗ giữ hết hạn giữa chừng và một tiến trình khác chạy lại cùng việc ⇒ gửi hai lần. Nhưng
    /// cũng không nên quá dài: tiến trình chết giữa chừng sẽ khoá message đúng bằng khoảng này.
    /// 5 phút phủ được cả timeout HTTP mặc định lẫn vài lần thử lại bên trong.
    /// </remarks>
    public int LeaseSeconds { get; set; } = 300;

    public bool FailOpenWhenRedisDown { get; set; } = false;
}
