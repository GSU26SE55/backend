using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

public class MarkNotificationReadCommandHandlerTests
{
    private static NotificationEntity Noti(Guid id, Guid userId, NotificationStatusEnum status, bool isDeleted = false) => new()
    {
        Id = id,
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Status = status,
        Title = "t",
        Body = "b",
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task MarkRead_OwnUnread_SetsRead_Returns200()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = Noti(id, userId, NotificationStatusEnum.Sent);
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: new[] { entity });
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        var resp = await handler.Handle(new MarkNotificationReadCommand { Id = id, UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(id);
        entity.Status.Should().Be(NotificationStatusEnum.Read);
        entity.ReadAt.Should().NotBeNull();
        notifications.Verify(r => r.UpdateAsync(entity), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_Idempotent_Returns200_NoWrite()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = Noti(id, userId, NotificationStatusEnum.Read);
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: new[] { entity });
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        var resp = await handler.Handle(new MarkNotificationReadCommand { Id = id, UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(id);
        notifications.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_NotFound_Returns404_NoWrite()
    {
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build();
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new MarkNotificationReadCommand { Id = Guid.NewGuid(), UserId = Guid.NewGuid() },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
        notifications.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_OfAnotherUser_Returns404_NotModified()
    {
        var id = Guid.NewGuid();
        var entity = Noti(id, Guid.NewGuid(), NotificationStatusEnum.Sent); // chủ khác
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(notificationSeed: new[] { entity });
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        var resp = await handler.Handle(
            new MarkNotificationReadCommand { Id = id, UserId = Guid.NewGuid() },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
        entity.Status.Should().Be(NotificationStatusEnum.Sent);
        entity.ReadAt.Should().BeNull();
        notifications.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
    }

    [Fact]
    public async Task MarkRead_SoftDeleted_Returns404()
    {
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var entity = Noti(id, userId, NotificationStatusEnum.Sent, isDeleted: true);
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: new[] { entity });
        var handler = new MarkNotificationReadCommandHandler(uow.Object);

        var resp = await handler.Handle(new MarkNotificationReadCommand { Id = id, UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(404);
    }
}
