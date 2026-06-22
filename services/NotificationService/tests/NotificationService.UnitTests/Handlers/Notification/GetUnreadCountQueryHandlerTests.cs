using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

public class GetUnreadCountQueryHandlerTests
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
    public async Task UnreadCount_CountsOnlyOwnUndeletedUnread()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var seed = new[]
        {
            Noti(userId, NotificationStatusEnum.Pending),
            Noti(userId, NotificationStatusEnum.Sent),
            Noti(userId, NotificationStatusEnum.Read),               // đã đọc → không tính
            Noti(userId, NotificationStatusEnum.Sent, isDeleted: true), // đã xóa → không tính
            Noti(otherId, NotificationStatusEnum.Sent)               // user khác → không tính
        };
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new GetUnreadCountQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetUnreadCountQuery { UserId = userId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(2);
    }

    [Fact]
    public async Task UnreadCount_Empty_ReturnsZero()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var handler = new GetUnreadCountQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetUnreadCountQuery { UserId = Guid.NewGuid() }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        resp.Data.Should().Be(0);
    }
}
