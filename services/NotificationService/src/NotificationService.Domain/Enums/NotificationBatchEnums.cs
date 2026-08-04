namespace NotificationService.Domain.Enums;

/// <summary>Sprint 6.4 NOTI4-06 — một lần gửi bắt nguồn từ đâu. Enum bắt đầu từ 1.</summary>
public enum NotificationBatchSourceEnum
{
    /// <summary>Sinh tự động từ một sự kiện nghiệp vụ (consumer RabbitMQ).</summary>
    Event = 1,

    /// <summary>Admin bấm gửi từ giao diện quản trị.</summary>
    Manual = 2,
}

/// <summary>Sprint 6.4 NOTI4-06 — trạng thái nở người nhận của một lần gửi. Enum bắt đầu từ 1.</summary>
public enum NotificationBatchStatusEnum
{
    /// <summary>Đã tạo bản ghi lần gửi nhưng chưa sinh dòng notification nào.</summary>
    Pending = 1,

    /// <summary>Đã nở xong: mỗi người nhận × mỗi kênh một dòng notification.</summary>
    FannedOut = 2,

    /// <summary>Nở thất bại. Hiện chỉ đạt tới trạng thái này khi fan-out chạy nền (xem §17.6.4.5 fork 1).</summary>
    Failed = 3,
}

/// <summary>Sprint 6.4 NOTI4-06 — một lần gửi nhắm tới cái gì. Enum bắt đầu từ 1.</summary>
public enum NotificationBatchTargetKindEnum
{
    /// <summary>Nhắm cả một nhóm.</summary>
    Group = 1,

    /// <summary>Nhắm đích danh một người, ngoài các nhóm đã chọn.</summary>
    User = 2,
}
