namespace TicketService.Domain.Enums;

/// <summary>
/// Các hành động ghi nhận trong lịch sử (Timeline) của Ticket.
/// </summary>
public enum ActivityActionEnum
{
    /// <summary>Ticket được tạo mới.</summary>
    Created = 1,
    /// <summary>Thay đổi trạng thái.</summary>
    StatusChanged = 2,
    /// <summary>Đã gán mức độ ưu tiên.</summary>
    PriorityAssigned = 3,
    /// <summary>Đã gán nhân viên xử lý.</summary>
    StaffAssigned = 4,
    /// <summary>Đã điều chuyển nhân viên xử lý.</summary>
    StaffReassigned = 5,
    /// <summary>Đã thêm tin nhắn chat.</summary>
    Chatted = 6,
    /// <summary>Đã thêm nhật ký bảo trì.</summary>
    MaintenanceLogged = 7,
    /// <summary>Đã đính kèm tệp.</summary>
    AttachmentAdded = 8,
    /// <summary>Tạm dừng tính SLA.</summary>
    SlaPaused = 9,
    /// <summary>Tiếp tục tính SLA.</summary>
    SlaResumed = 10,
    /// <summary>Cảnh báo sắp quá hạn SLA.</summary>
    SlaWarning = 11,
    /// <summary>Đã vi phạm SLA.</summary>
    SlaBreached = 12,
    /// <summary>Yêu cầu chuyển cấp.</summary>
    EscalationRequested = 13,
    /// <summary>Đã chuyển cấp xử lý.</summary>
    Escalated = 14,
    /// <summary>Đã đánh dấu là sự cố (Incident).</summary>
    IncidentDeclared = 15,
    /// <summary>Nhân viên báo cáo hoàn thành.</summary>
    Resolved = 16,
    /// <summary>Quản lý phê duyệt kết quả.</summary>
    Approved = 17,
    /// <summary>Quản lý từ chối kết quả.</summary>
    Rejected = 18,
    /// <summary>Khách hàng đánh giá.</summary>
    Rated = 19,
    /// <summary>Khách hàng mở lại ticket.</summary>
    Reopened = 20,
    /// <summary>Ticket đã đóng chính thức.</summary>
    Closed = 25,
    /// <summary>Hệ thống tự động đóng.</summary>
    AutoClosed = 22,
    /// <summary>Nhân viên cấp cao giải quyết.</summary>
    ResolvedByEscalatedStaff = 23,
    /// <summary>Phê duyệt triage.</summary>
    TriageApproved = 24,
    /// <summary>Đã chỉnh sửa tin nhắn chat.</summary>
    ChatEdited = 26,
    /// <summary>Đã xóa tin nhắn chat.</summary>
    ChatDeleted = 27
}
