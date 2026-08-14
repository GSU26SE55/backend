using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — tuỳ chọn của một người dùng cho MỘT nhóm notification.
///
/// Mỗi user có tối đa 6 record (một cho mỗi <see cref="NotificationCategoryEnum"/>).
/// **Không có record = chưa tuỳ chỉnh**, khi đó dispatcher rơi về
/// <see cref="NotificationPreference"/> mức kênh (hành vi trước sprint này). Nhờ vậy dữ liệu cũ
/// và FE cũ vẫn chạy đúng, không cần backfill.
///
/// Quan hệ với <see cref="NotificationPreference"/>: **và logic** — kênh phải bật ở cả hai cấp thì
/// mới gửi. Tắt Email toàn cục vẫn thắng mọi tuỳ chọn nhóm; đó là cái người dùng mong đợi khi họ
/// gạt công tắc lớn.
/// </summary>
public class NotificationCategoryPreference : AuditableEntity
{
    public Guid UserId { get; set; }

    public NotificationCategoryEnum Category { get; set; }

    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; }
    public bool InAppEnabled { get; set; } = true;
}
