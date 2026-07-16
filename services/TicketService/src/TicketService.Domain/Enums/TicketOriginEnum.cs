namespace TicketService.Domain.Enums;

public enum TicketOriginEnum
{
    ManualByCustomer = 1,
    AutoFromAlert = 2,
    CreatedByStaff = 3,

    /// <summary>
    /// Sprint Bonus NS-13 (#657, R2, Q8=A) — ticket do hệ thống tự tạo (không từ 1 alert cụ thể),
    /// vd cascade risk High mà pin chưa có ticket active. ⚠️ Wire value — FE cần mirror giá trị 4.
    /// </summary>
    System = 4
}
