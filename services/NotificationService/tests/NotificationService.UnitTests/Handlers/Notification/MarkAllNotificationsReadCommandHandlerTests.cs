using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

public class MarkAllNotificationsReadCommandHandlerTests
{
    private static NotificationEntity Noti(Guid userId, NotificationStatusEnum status, bool isDeleted = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = status,
        Title = "t",
        Body = "b",
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task MarkAll_MarksOnlyOwnUnread_Returns200_WithCount()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var seed = new[]
        {
            Noti(userId, NotificationStatusEnum.Pending),
            Noti(userId, NotificationStatusEnum.Sent),
            Noti(userId, NotificationStatusEnum.Failed),
            Noti(userId, NotificationStatusEnum.Read),               // đã đọc → bỏ qua
            Noti(userId, NotificationStatusEnum.Sent, isDeleted: true), // đã xóa → bỏ qua
            Noti(otherId, NotificationStatusEnum.Sent)               // user khác → bỏ qua
        };
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new MarkAllNotificationsReadCommandHandler(uow.Object);

        var resp = await handler.Handle(new MarkAllNotificationsReadCommand { UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(3);
        notifications.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Exactly(3));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        seed.Where(n => n.UserId == userId && !n.IsDeleted && n.ReadAt != null)
            .Should().OnlyContain(n => n.Status == NotificationStatusEnum.Read);
    }

    [Fact]
    public async Task MarkAll_NoUnread_Returns200_Zero_NoWrite()
    {
        var userId = Guid.NewGuid();
        var seed = new[] { Noti(userId, NotificationStatusEnum.Read) };
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new MarkAllNotificationsReadCommandHandler(uow.Object);

        var resp = await handler.Handle(new MarkAllNotificationsReadCommand { UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(0);
        notifications.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
