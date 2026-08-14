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
                    "Delivers only through the system's SignalR hub. No EAS/FCM keys or device token required; "
                    + "the receiving device must be connected to the hub, or it will re-fetch via REST when the app opens.",
                RequiresDeviceToken = false,
            },
            new PushTransportOptionDto
            {
                Value = PushTransportEnum.Expo,
                Name = nameof(PushTransportEnum.Expo),
                Description =
                    "Delivers only through the Expo Push API. Requires an active device token; in exchange, "
                    + "delivery receipts are reconciled so notifications can reach the Delivered status.",
                RequiresDeviceToken = true,
            },
            new PushTransportOptionDto
            {
                Value = PushTransportEnum.Both,
                Name = nameof(PushTransportEnum.Both),
                Description =
                    "Delivers through both channels for the same notification, succeeding when at least one "
                    + "channel succeeds. Devices with the app open receive it instantly via SignalR; devices "
                    + "with the app closed still receive it via Expo.",
                RequiresDeviceToken = false,
            },
        ],
    };
}
