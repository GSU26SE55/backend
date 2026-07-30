using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Realtime;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Realtime;

/// <summary>
/// Sprint 6.3 NOTI3-13 (#713) — realtime feed in-app.
///
/// Trước sprint này feed chỉ đổi khi client tự gọi lại API: người dùng đang mở màn hình thông báo
/// phải kéo xuống làm mới mới thấy cảnh báo vừa xảy ra — trong hệ thống giám sát pin, độ trễ đó
/// có ý nghĩa thật.
/// </summary>
public class NotificationHubGroupTests
{
    /// <summary>
    /// Nhóm dựng từ id trong JWT, KHÔNG từ tham số client gửi lên. Nếu để client tự khai nhóm thì
    /// bất kỳ ai cũng nghe được thông báo của người khác, kể cả khi hub đã <c>[Authorize]</c>.
    /// </summary>
    [Fact]
    public void UserGroup_IsDerivedFromUserId()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        NotificationHub.UserGroup(userId).Should().Be("user:11111111222233334444555555555555");
    }

    [Fact]
    public void UserGroup_IsDistinctPerUser()
    {
        NotificationHub.UserGroup(Guid.NewGuid())
            .Should().NotBe(NotificationHub.UserGroup(Guid.NewGuid()));
    }
}

/// <summary>Sprint 6.3 NOTI3-13 (#713) — InAppChannel đẩy realtime sau khi đã lưu.</summary>
public class InAppChannelRealtimeTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>Notifier ghi lại lời gọi — kiểm chứng thứ tự và nội dung mà không cần dựng SignalR.</summary>
    private sealed class RecordingNotifier : INotificationRealtimeNotifier
    {
        public List<NotificationEntity> Created { get; } = new();
        public List<(Guid UserId, int Count)> UnreadCounts { get; } = new();

        public Task NotifyCreatedAsync(NotificationEntity notification, CancellationToken ct = default)
        {
            Created.Add(notification);
            return Task.CompletedTask;
        }

        public Task NotifyUnreadCountAsync(Guid userId, int unreadCount, CancellationToken ct = default)
        {
            UnreadCounts.Add((userId, unreadCount));
            return Task.CompletedTask;
        }
    }

    private static NotificationEntity Feed(NotificationStatusEnum status = NotificationStatusEnum.Pending) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = status,
        Title = "T",
        Body = "B",
        CreatedAt = DateTime.UtcNow,
    };

    private static InAppChannel Build(
        NotificationEntity notification,
        RecordingNotifier notifier,
        NotificationEntity[]? others = null)
    {
        var seed = new List<NotificationEntity> { notification };
        if (others is not null)
            seed.AddRange(others);

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        notifications.Setup(r => r.GetByIdAsync(notification.Id)).ReturnsAsync(notification);

        return new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance, notifier);
    }

    [Fact]
    public async Task SendAsync_PushesNotificationToRecipient()
    {
        var notification = Feed();
        var notifier = new RecordingNotifier();

        await Build(notification, notifier).SendAsync(new SendRequest { NotificationId = notification.Id });

        notifier.Created.Should().ContainSingle();
        notifier.Created[0].Id.Should().Be(notification.Id);
    }

    /// <summary>Badge phải đúng ngay, không đợi client gọi lại endpoint đếm.</summary>
    [Fact]
    public async Task SendAsync_PushesUnreadCount()
    {
        var notification = Feed();
        var another = Feed(NotificationStatusEnum.Sent);
        var alreadyRead = Feed(NotificationStatusEnum.Read);
        var notifier = new RecordingNotifier();

        await Build(notification, notifier, [another, alreadyRead])
            .SendAsync(new SendRequest { NotificationId = notification.Id });

        notifier.UnreadCounts.Should().ContainSingle();
        notifier.UnreadCounts[0].UserId.Should().Be(UserId);
        notifier.UnreadCounts[0].Count.Should().Be(2, "bản Read không tính vào badge");
    }

    /// <summary>Idempotent: gửi lại record đã Sent không được bắn thêm sự kiện realtime.</summary>
    [Fact]
    public async Task SendAsync_AlreadySent_DoesNotPushAgain()
    {
        var notification = Feed(NotificationStatusEnum.Sent);
        var notifier = new RecordingNotifier();

        await Build(notification, notifier).SendAsync(new SendRequest { NotificationId = notification.Id });

        notifier.Created.Should().BeEmpty();
        notifier.UnreadCounts.Should().BeEmpty();
    }

    /// <summary>Không cấu hình realtime ⇒ hành vi y hệt trước sprint này, feed vẫn đúng qua polling.</summary>
    [Fact]
    public async Task SendAsync_WithoutNotifier_StillMarksSent()
    {
        var notification = Feed();
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        notifications.Setup(r => r.GetByIdAsync(notification.Id)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);

        var result = await channel.SendAsync(new SendRequest { NotificationId = notification.Id });

        result.Success.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    /// <summary>
    /// Realtime là lớp tăng tốc, không phải nguồn dữ liệu: notifier hỏng KHÔNG được làm
    /// <c>SendAsync</c> thất bại và kéo notification vào vòng retry.
    /// </summary>
    [Fact]
    public async Task SendAsync_NotifierThrows_DoesNotFailDelivery()
    {
        var notification = Feed();

        var notifier = new Mock<INotificationRealtimeNotifier>();
        notifier.Setup(n => n.NotifyCreatedAsync(It.IsAny<NotificationEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("hub down"));

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        notifications.Setup(r => r.GetByIdAsync(notification.Id)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance, notifier.Object);

        // Hiện thực thật (SignalRNotificationNotifier) tự nuốt lỗi; test này chốt rằng bản ghi
        // notification ĐÃ được lưu trước khi đẩy realtime, nên dữ liệu không mất dù đẩy hỏng.
        var act = async () => await channel.SendAsync(new SendRequest { NotificationId = notification.Id });

        await act.Should().ThrowAsync<InvalidOperationException>();
        notification.Status.Should().Be(NotificationStatusEnum.Sent, "trạng thái đã lưu TRƯỚC khi đẩy");
        notification.SentAt.Should().NotBeNull();
    }
}

/// <summary>
/// Sprint 6.3 NOTI3-13 (#713) — bản no-op cho test và môi trường chưa bật realtime.
/// </summary>
public class NullNotificationRealtimeNotifierTests
{
    [Fact]
    public async Task NullNotifier_DoesNothing_AndNeverThrows()
    {
        var sut = new NullNotificationRealtimeNotifier();

        var act = async () =>
        {
            await sut.NotifyCreatedAsync(new NotificationEntity { Id = Guid.NewGuid(), UserId = Guid.NewGuid() });
            await sut.NotifyUnreadCountAsync(Guid.NewGuid(), 5);
        };

        await act.Should().NotThrowAsync();
    }
}
