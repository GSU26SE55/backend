namespace TicketService.Domain.Enums;

/// <summary>
/// Trạng thái của một Ticket trong hệ thống.
/// </summary>
public enum TicketStatusEnum
{
    /// <summary>Vừa tạo, chờ triage.</summary>
    New = 1,
    /// <summary>Đã triage sơ bộ, chờ Manager phê duyệt.</summary>
    Open = 2,
    /// <summary>Đã gán Staff, chờ Staff xác nhận.</summary>
    Assigned = 3,
    /// <summary>Staff đang xử lý.</summary>
    InProgress = 4,
    /// <summary>Tạm dừng: Chờ khách hàng phản hồi.</summary>
    WaitingCustomer = 5,
    /// <summary>Tạm dừng: Chờ linh kiện.</summary>
    WaitingParts = 6,
    /// <summary>Tạm dừng: Chờ lịch hẹn tại chỗ.</summary>
    WaitingOnsiteSchedule = 7,
    /// <summary>Staff báo đã xong, chờ Manager phê duyệt.</summary>
    Resolved = 8,
    /// <summary>Đã chuyển cấp xử lý cho Senior/Expert.</summary>
    Escalated = 9,
    /// <summary>Manager đã phê duyệt kết quả, chờ Customer đánh giá.</summary>
    ClosedPendingRate = 10,
    /// <summary>Đã đóng chính thức.</summary>
    Closed = 11,
    /// <summary>Manager từ chối kết quả (quay lại InProgress).</summary>
    ClosedRejected = 12,
    /// <summary>Sự cố nghiêm trọng.</summary>
    Incident = 13,
}
