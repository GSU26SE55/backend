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
    System = 4,

    /// <summary>
    /// Sự cố MÔI TRƯỜNG của site — thiết bị tự báo (khói, rò khí, ngập) hoặc backend chấm số đo
    /// ambient vượt <c>AmbientThresholdConfig</c> (nhiệt độ, độ ẩm, gas, combo).
    /// ⚠️ Wire value — FE + mobile cần mirror giá trị 5.
    ///
    /// <para>Trước đây nhóm này dùng chung <see cref="AutoFromAlert"/> (đường ambient) và
    /// <see cref="System"/> (đường incident). Cả hai đều là origin của thứ khác — `AutoFromAlert`
    /// nghĩa là "AI chấm bất thường của một viên pin" — nên mọi nơi phân loại nguồn phải nhớ thêm
    /// ngoại lệ <c>ImpactScope == Site</c> để gỡ ra. Chỗ nào quên là ticket nhiệt độ của CẢ SITE
    /// hiện như ticket AI đoán cho MỘT viên pin: đã xảy ra ở bộ lọc nguồn, rồi lặp lại y hệt ở
    /// badge "AI suggested" trên hàng chờ của Manager.
    ///
    /// Có origin riêng thì việc phân loại đọc thẳng một field, không còn chỗ để quên.</para>
    /// </summary>
    AutoFromEnvironment = 5
}
