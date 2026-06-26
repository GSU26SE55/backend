using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// Builder gom mock <see cref="INotificationUnitOfWork"/> + các repository thường dùng trong test handler.
/// Dùng MockQueryable.Moq (<c>.BuildMock()</c>) để mock IQueryable async cho <c>GetAllAsync()</c>.
/// </summary>
public static class MockNotificationUnitOfWork
{
    public static (Mock<INotificationUnitOfWork> uow,
                   Mock<IGenericRepository<DeviceToken>> deviceTokens,
                   Mock<IGenericRepository<Notification>> notifications)
        Build(
            IEnumerable<DeviceToken>? deviceTokenSeed = null,
            IEnumerable<Notification>? notificationSeed = null)
    {
        var deviceTokenData = (deviceTokenSeed ?? Array.Empty<DeviceToken>()).ToArray();
        var deviceTokens = new Mock<IGenericRepository<DeviceToken>>();
        deviceTokens.Setup(r => r.GetAllAsync())
            .Returns(deviceTokenData.AsQueryable().BuildMock());
        deviceTokens.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(deviceTokenData.AsQueryable().BuildMock());

        var notificationData = (notificationSeed ?? Array.Empty<Notification>()).ToArray();
        var notifications = new Mock<IGenericRepository<Notification>>();
        notifications.Setup(r => r.GetAllAsync())
            .Returns(notificationData.AsQueryable().BuildMock());
        notifications.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(notificationData.AsQueryable().BuildMock());

        var preferences = new Mock<IGenericRepository<NotificationPreference>>();
        preferences.Setup(r => r.GetAllAsync())
            .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());
        preferences.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());

        var templates = new Mock<IGenericRepository<NotificationTemplate>>();
        templates.Setup(r => r.GetAllAsync())
            .Returns(Array.Empty<NotificationTemplate>().AsQueryable().BuildMock());
        templates.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(Array.Empty<NotificationTemplate>().AsQueryable().BuildMock());

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.DeviceTokens).Returns(deviceTokens.Object);
        uow.SetupGet(u => u.Notifications).Returns(notifications.Object);
        uow.SetupGet(u => u.NotificationPreferences).Returns(preferences.Object);
        uow.SetupGet(u => u.NotificationTemplates).Returns(templates.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, deviceTokens, notifications);
    }
}
