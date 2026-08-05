using NotificationService.Application.DTOs.Response.Setting;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Setting;

/// <summary>
/// Dựng <see cref="PushTransportDto"/> cho cả handler đọc lẫn handler ghi.
///
/// <para>Danh sách lựa chọn nằm ở backend chứ không ở frontend: thêm một đường vận chuyển mới mà
/// frontend hard-code danh sách thì giao diện sẽ thiếu lựa chọn đó cho tới khi ai đó nhớ ra phải
/// sửa cả hai nơi.</para>
/// </summary>
internal static class PushTransportDtoFactory
{
    public static PushTransportDto Build(PushTransportEnum current) => new()
    {
        Transport = current,
        TransportName = current.ToString(),
        Options =
        [
            new PushTransportOptionDto
            {
                Value = PushTransportEnum.SignalR,
                Name = nameof(PushTransportEnum.SignalR),
                Description =
                    "Chỉ đẩy qua hub SignalR của hệ thống. Không cần khoá EAS/FCM và không cần device token; "
                    + "máy nhận phải đang kết nối hub hoặc sẽ đọc lại qua REST khi mở app.",
                RequiresDeviceToken = false,
            },
            new PushTransportOptionDto
            {
                Value = PushTransportEnum.Expo,
                Name = nameof(PushTransportEnum.Expo),
                Description =
                    "Chỉ đẩy qua Expo Push API. Cần device token còn hoạt động; đổi lại có đối soát biên nhận "
                    + "nên thông báo mới lên được trạng thái Delivered.",
                RequiresDeviceToken = true,
            },
            new PushTransportOptionDto
            {
                Value = PushTransportEnum.Both,
                Name = nameof(PushTransportEnum.Both),
                Description =
                    "Đẩy cả hai đường cho cùng một thông báo, thành công khi ít nhất một đường thành công. "
                    + "Máy đang mở app nhận tức thì qua SignalR, máy đã tắt app vẫn nhận qua Expo.",
                RequiresDeviceToken = false,
            },
        ],
    };
}
