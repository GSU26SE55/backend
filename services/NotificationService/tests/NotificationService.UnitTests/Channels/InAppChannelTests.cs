using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Channels;

public class InAppChannelTests
{
    private static SendRequest MakeRequest(Guid notificationId) => new()
    {
        NotificationId = notificationId,
        UserId = Guid.NewGuid(),
        Title = "Test",
        Body = "Body"
    };

    [Fact]
    public async Task SendAsync_NotificationFound_SetsStatusSentAndReturnsSuccess()
    {
        var notificationId = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = notificationId,
            Status = NotificationStatusEnum.Pending,
            Title = "Test",
            Body = "Body"
        };

        var (uow, _, notificationRepo) = MockNotificationUnitOfWork.Build(notificationSeed: [notification]);
        uow.Setup(u => u.Notifications.GetByIdAsync(notificationId)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(notificationId));

        result.Success.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatusEnum.Sent);
        notification.SentAt.Should().NotBeNull();
        notificationRepo.Verify(r => r.UpdateAsync(notification), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AlreadySent_ReturnsSuccessWithoutUpdate()
    {
        var notificationId = Guid.NewGuid();
        var notification = new NotificationEntity
        {
            Id = notificationId,
            Status = NotificationStatusEnum.Sent,
            SentAt = DateTime.UtcNow.AddMinutes(-1)
        };

        var (uow, _, notificationRepo) = MockNotificationUnitOfWork.Build();
        uow.Setup(u => u.Notifications.GetByIdAsync(notificationId)).ReturnsAsync(notification);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(notificationId));

        result.Success.Should().BeTrue();
        notificationRepo.Verify(r => r.UpdateAsync(It.IsAny<NotificationEntity>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_NotificationNotFound_ReturnsFailure()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        uow.Setup(u => u.Notifications.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((NotificationEntity?)null);

        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest(Guid.NewGuid()));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Notification not found");
    }

    [Fact]
    public void ChannelType_IsInApp()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new InAppChannel(uow.Object, NullLogger<InAppChannel>.Instance);
        channel.ChannelType.Should().Be(NotificationChannelEnum.InApp);
    }
}
