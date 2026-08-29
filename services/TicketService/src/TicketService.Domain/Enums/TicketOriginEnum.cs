namespace TicketService.Domain.Enums;

public enum TicketOriginEnum
{
    ManualByCustomer = 1,
    AutoFromAlert = 2,
    // 3 = CreatedByStaff (đã bỏ): staff tạo hộ khách nay ghi thẳng ManualByCustomer.
    // KHÔNG tái sử dụng giá trị 3 và KHÔNG đánh số lại các member dưới — cột origin lưu
    // dạng int, đổi số là mọi dòng cũ nhảy sang loại khác.

    /// <summary>
    /// Sprint Bonus NS-13 (#657, R2, Q8=A) — ticket do hệ thống tự tạo (không từ 1 alert cụ thể),
    /// vd cascade risk High mà pin chưa có ticket active. ⚠️ Wire value — FE cần mirror giá trị 4.
    /// </summary>
    System = 4
}
