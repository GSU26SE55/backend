using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

/// <summary>
/// Sprint 6.3 NOTI3-14 (#714) — <c>Opened</c> tách khỏi <c>Read</c>.
///
/// <c>Read</c> = user bấm "đã đọc" trên feed (có thể chỉ lướt qua).
/// <c>Opened</c> = user thực sự mở nội dung từ push/deep link — chỉ số này mới đo được
/// hiệu quả thật của kênh push. Hạ Opened xuống Read là mất thông tin không lấy lại được.
/// </summary>
public class MarkNotificationOpenedCommandHandlerTests
{
    private static NotificationEntity Noti(
        Guid id, Guid userId, NotificationStatusEnum status,
        NotificationChannelEnum channel = NotificationChannelEnum.Push) => new()
        {
            Id = id,
            UserId = userId,
            Type = NotificationTypeEnum.TicketCreated,
            Channel = channel,
            Status = status,
            Title = "t",
            Body = "b",
            EntityType = "Ticket",
            EntityId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task MarkOpened_OwnNotification_SetsOpened_Returns200()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = Noti(id, userId, NotificationStatusEnum.Delivered);

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: [entity]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        var resp = await handler.Handle(
            new MarkNotificationOpenedCommand { Id = id, UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        entity.Status.Should().Be(NotificationStatusEnum.Opened);
        entity.ReadAt.Should().NotBeNull("mở tức là đã đọc");
        notifications.Verify(r => r.UpdateAsync(entity), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Client mobile gửi lại khi mạng chập chờn — không được lỗi.</summary>
    [Fact]
    public async Task MarkOpened_AlreadyOpened_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = Noti(id, userId, NotificationStatusEnum.Opened);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [entity]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        var resp = await handler.Handle(
            new MarkNotificationOpenedCommand { Id = id, UserId = userId }, CancellationToken.None);

        resp.StatusCode.Should().Be(200);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Notification của người khác → 404, không được lộ là nó tồn tại (IDOR).</summary>
    [Fact]
    public async Task MarkOpened_OtherUsersNotification_Returns404()
    {
        var id = Guid.NewGuid();
        var entity = Noti(id, Guid.NewGuid(), NotificationStatusEnum.Sent);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [entity]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        var resp = await handler.Handle(
            new MarkNotificationOpenedCommand { Id = id, UserId = Guid.NewGuid() }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
        entity.Status.Should().Be(NotificationStatusEnum.Sent);
    }

    [Fact]
    public async Task MarkOpened_NotFound_Returns404()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        var resp = await handler.Handle(
            new MarkNotificationOpenedCommand { Id = Guid.NewGuid(), UserId = Guid.NewGuid() },
            CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// Mở trên máy ⇒ các bản email/sms còn Pending của cùng sự kiện phải dừng lại,
    /// nếu không user đã xử lý xong vẫn lãnh thêm email cho đúng việc đó.
    /// </summary>
    [Fact]
    public async Task MarkOpened_PropagatesToSiblingChannels()
    {
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var push = Noti(Guid.NewGuid(), userId, NotificationStatusEnum.Delivered);
        push.EntityId = entityId;
        push.CreatedAt = now;

        var email = Noti(Guid.NewGuid(), userId, NotificationStatusEnum.Pending, NotificationChannelEnum.Email);
        email.EntityType = push.EntityType;
        email.EntityId = entityId;
        email.CreatedAt = now;
        email.NextAttemptAt = now.AddMinutes(5);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [push, email]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        await handler.Handle(
            new MarkNotificationOpenedCommand { Id = push.Id, UserId = userId }, CancellationToken.None);

        push.Status.Should().Be(NotificationStatusEnum.Opened);
        email.Status.Should().Be(NotificationStatusEnum.Read);
        email.NextAttemptAt.Should().BeNull("worker chỉ lấy record Pending có NextAttemptAt tới hạn");
    }

    /// <summary>Record Failed là dữ liệu chẩn đoán — ghi đè sẽ mất dấu vết lỗi.</summary>
    [Fact]
    public async Task MarkOpened_DoesNotOverwriteFailedSibling()
    {
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var push = Noti(Guid.NewGuid(), userId, NotificationStatusEnum.Sent);
        push.EntityId = entityId;
        push.CreatedAt = now;

        var sms = Noti(Guid.NewGuid(), userId, NotificationStatusEnum.Failed, NotificationChannelEnum.Sms);
        sms.EntityType = push.EntityType;
        sms.EntityId = entityId;
        sms.CreatedAt = now;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [push, sms]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        await handler.Handle(
            new MarkNotificationOpenedCommand { Id = push.Id, UserId = userId }, CancellationToken.None);

        sms.Status.Should().Be(NotificationStatusEnum.Failed);
    }

    /// <summary>Record cũ / record test có thể mang CreatedAt = MinValue — cửa sổ anh em không được tràn.</summary>
    [Fact]
    public async Task MarkOpened_HandlesMinValueCreatedAt_WithoutOverflow()
    {
        var userId = Guid.NewGuid();
        var entity = Noti(Guid.NewGuid(), userId, NotificationStatusEnum.Sent);
        entity.CreatedAt = DateTime.MinValue;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [entity]);
        var handler = new MarkNotificationOpenedCommandHandler(uow.Object, NoopAuditWriter.Instance);

        var act = async () => await handler.Handle(
            new MarkNotificationOpenedCommand { Id = entity.Id, UserId = userId }, CancellationToken.None);

        await act.Should().NotThrowAsync();
        entity.Status.Should().Be(NotificationStatusEnum.Opened);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000")]
    public async Task Validate_RejectsEmptyIds(string id, string userId)
    {
        var command = new MarkNotificationOpenedCommand { Id = Guid.Parse(id), UserId = Guid.Parse(userId) };

        var response = await command.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().NotBeEmpty();
    }
}

/// <summary>
/// Sprint 6.3 NOTI3-14 (#714) — <c>Opened</c> phải được coi là "đã xem" ở mọi nơi đếm chưa đọc,
/// nếu không badge sẽ báo số sai ngay sau khi user mở notification.
/// </summary>
public class OpenedCountsAsSeenTests
{
    private static NotificationEntity Feed(Guid userId, NotificationStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = status,
        Title = "t",
        Body = "b",
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task UnreadCount_ExcludesOpened()
    {
        var userId = Guid.NewGuid();
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed:
        [
            Feed(userId, NotificationStatusEnum.Sent),
            Feed(userId, NotificationStatusEnum.Opened),
            Feed(userId, NotificationStatusEnum.Read),
        ]);

        var handler = new GetUnreadCountQueryHandler(uow.Object);
        var resp = await handler.Handle(new GetUnreadCountQuery { UserId = userId }, CancellationToken.None);

        resp.Data.Should().Be(1, "chỉ record Sent còn là chưa đọc");
    }

    [Fact]
    public async Task UnreadOnlyFilter_ExcludesOpened()
    {
        var userId = Guid.NewGuid();
        var opened = Feed(userId, NotificationStatusEnum.Opened);
        var pending = Feed(userId, NotificationStatusEnum.Pending);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [opened, pending]);

        var handler = new GetNotificationsQueryHandler(uow.Object);
        var resp = await handler.Handle(
            new GetNotificationsQuery { UserId = userId, UnreadOnly = true }, CancellationToken.None);

        resp.Data!.Items.Should().ContainSingle();
        resp.Data.Items.First().Id.Should().Be(pending.Id.ToString());
    }

    /// <summary>read-all không được hạ Opened xuống Read.</summary>
    [Fact]
    public async Task MarkAllRead_DoesNotDowngradeOpened()
    {
        var userId = Guid.NewGuid();
        var opened = Feed(userId, NotificationStatusEnum.Opened);
        var sent = Feed(userId, NotificationStatusEnum.Sent);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [opened, sent]);
        var handler = new MarkAllNotificationsReadCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new MarkAllNotificationsReadCommand { UserId = userId }, CancellationToken.None);

        resp.Data.Should().Be(1);
        opened.Status.Should().Be(NotificationStatusEnum.Opened);
        sent.Status.Should().Be(NotificationStatusEnum.Read);
    }
}
