namespace NotificationService.Domain.Enums;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — nhóm nghiệp vụ của notification.
///
/// **Vì sao cần:** trước sprint này người dùng chỉ bật/tắt được **cả kênh** (tắt Email là tắt sạch,
/// kể cả email SLA sắp vỡ). Chuẩn ngành (Slack, GitHub, Atlassian) cho chọn theo **ma trận
/// nhóm × kênh**: "chat thì chỉ in-app, SLA thì email + push". Không có nhóm thì người dùng buộc
/// phải chọn giữa bị làm phiền và bỏ lỡ việc quan trọng — và họ luôn chọn tắt hết.
///
/// 32 giá trị của <see cref="NotificationTypeEnum"/> gom lại thành 6 nhóm này; bảng ánh xạ đặt ở
/// <c>NotificationCategoryMap</c>.
/// </summary>
public enum NotificationCategoryEnum
{
    /// <summary>Vòng đời ticket: tạo / gán / đổi trạng thái / duyệt / từ chối / mở lại / đánh giá.</summary>
    Ticket = 1,

    /// <summary>SLA và leo thang: cảnh báo sắp vỡ, đã vỡ, escalate, sự cố nghiêm trọng.</summary>
    Sla = 2,

    /// <summary>Sức khoẻ pin và thiết bị IoT: bất thường, rủi ro lan truyền, mất heartbeat.</summary>
    Battery = 3,

    /// <summary>Sự cố môi trường tại site (nhiệt độ / độ ẩm / ngập …).</summary>
    Environmental = 4,

    /// <summary>Trao đổi trên ticket: chat, mention, reaction, thay đổi người tham gia.</summary>
    Chat = 5,

    /// <summary>Tài khoản và hệ thống: kích hoạt, mời admin, thông báo hệ thống.</summary>
    Account = 6,
}
