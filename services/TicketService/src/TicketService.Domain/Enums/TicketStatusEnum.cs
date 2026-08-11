namespace TicketService.Domain.Enums;

/// <summary>
/// Trạng thái của một Ticket trong hệ thống.
/// </summary>
public enum TicketStatusEnum
{
    //open, inprogress, peding, completed, Closed, ClosedRejected, Request,  Escalated ->  chuyển thành REAssign, P3 -> T1. P2 -> T2, P1 -> T3
    /// <summary>Vừa tạo, chờ triage.</summary>
    New = 1, //bo
    /// <summary>Đã triage sơ bộ, chờ Manager phê duyệt.</summary>
    Open = 2,
    /// <summary>Đã gán Staff, chờ Staff xác nhận.</summary>
    Assigned = 3, //gop inprogess
    /// <summary>Staff đang xử lý.</summary>
    InProgress = 4,
    /// <summary>Tạm dừng: Chờ khách hàng phản hồi.</summary>
    WaitingCustomer = 5, // thay pending se chay neu khach hang ko co nha
    /// <summary>Tạm dừng: Chờ linh kiện.</summary>
    WaitingParts = 6, //bo
    /// <summary>Tạm dừng: Chờ lịch hẹn tại chỗ.</summary>
    WaitingOnsiteSchedule = 7,//bo
    /// <summary>Staff báo đã xong, chờ Manager phê duyệt.</summary>
    Resolved = 8, //completed
    /// <summary>Đã chuyển cấp xử lý cho Senior/Expert.</summary>
    Escalated = 9,
    /// <summary>Manager đã phê duyệt kết quả, chờ Customer đánh giá.</summary>
    ClosedPendingRate = 10, // bo chuyen truc tiep ve closed va cho danh gia trong 7 ngay
    /// <summary>Đã đóng chính thức.</summary>
    Closed = 11,
    /// <summary>Manager từ chối kết quả (quay lại InProgress).</summary>
    ClosedRejected = 12,
    /// <summary>Sự cố nghiêm trọng.</summary> Urgen. P Urgency x Impact
    Incident = 13, // chuyen sang cap cao nhat priority tren ca P1 -> tao mot p hoac trang thai trong P va` ghi laf Incident
}
