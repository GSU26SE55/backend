using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Khoá cấu hình cấp hệ thống lưu trong bảng <c>notification_settings</c>.
/// Khai báo tập trung ở đây để không rải chuỗi thô khắp nơi rồi gõ sai một chữ là đọc ra mặc định
/// mà không ai biết.
/// </summary>
public static class NotificationSettingKeys
{
    /// <summary>Đường vận chuyển của kênh Push — giá trị là tên <see cref="PushTransportEnum"/>.</summary>
    public const string PushTransport = "push.transport";
}

/// <summary>
/// Đọc/ghi đường vận chuyển push (<see cref="PushTransportEnum"/>) ở cấp hệ thống.
///
/// <para>Nguồn sự thật là bảng <c>notification_settings</c> chứ không phải appsettings, để màn hình
/// Admin đổi được lúc chạy. Giá trị trong appsettings chỉ đóng vai trò MẶC ĐỊNH cho lần chạy đầu
/// khi bảng còn trống.</para>
/// </summary>
public interface IPushTransportSettingService
{
    /// <summary>
    /// Đường vận chuyển đang áp dụng. Có cache ngắn nên đọc mỗi lần gửi không thành truy vấn DB.
    /// Không đọc được (DB lỗi, giá trị rác) thì trả mặc định thay vì ném — mất khả năng đổi cấu hình
    /// không được phép làm chết luôn đường gửi thông báo.
    /// </summary>
    Task<PushTransportEnum> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi đường vận chuyển và xoá cache ngay, để lần gửi kế tiếp dùng giá trị mới.
    /// </summary>
    Task SetAsync(PushTransportEnum transport, CancellationToken cancellationToken = default);
}
