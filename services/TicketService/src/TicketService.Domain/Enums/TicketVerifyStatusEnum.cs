namespace TicketService.Domain.Enums;

/// <summary>
/// Trạng thái AI verify ticket thủ công — AI chấm điểm thật/rác (human-in-the-loop, KHÔNG tự chặn).
/// ⚠️ Wire value — FE cần mirror.
/// </summary>
public enum TicketVerifyStatusEnum
{
    /// <summary>Chưa verify (vừa tạo, consumer async chưa xử lý xong).</summary>
    Pending = 1,

    /// <summary>AI đánh giá ticket hợp lệ (mô tả rõ ràng / khớp bất thường sensor thật).</summary>
    Legitimate = 2,

    /// <summary>AI nghi ticket rác (mô tả rỗng/spam / không khớp sensor). Manager xem trước khi triage.</summary>
    Suspicious = 3,

    /// <summary>Không verify được (AI service down / lookup fail) — ticket vẫn hợp lệ, chỉ bỏ qua verify.</summary>
    Skipped = 4
}
