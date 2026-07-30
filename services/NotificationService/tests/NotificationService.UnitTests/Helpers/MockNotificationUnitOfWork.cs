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
            IEnumerable<Notification>? notificationSeed = null,
            IEnumerable<AccountReadModel>? accountSeed = null,
            IEnumerable<NotificationTemplate>? templateSeed = null,
            IEnumerable<PushReceipt>? pushReceiptSeed = null,
            IEnumerable<NotificationCategoryPreference>? categoryPreferenceSeed = null)
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

        var templateData = (templateSeed ?? Array.Empty<NotificationTemplate>()).ToArray();
        var templates = new Mock<IGenericRepository<NotificationTemplate>>();
        templates.Setup(r => r.GetAllAsync())
            .Returns(templateData.AsQueryable().BuildMock());
        templates.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(templateData.AsQueryable().BuildMock());

        // Sprint 6.2 — dispatcher tra email/số điện thoại người nhận từ read-model account.
        var accountData = (accountSeed ?? Array.Empty<AccountReadModel>()).ToArray();
        var accounts = new Mock<IGenericRepository<AccountReadModel>>();
        accounts.Setup(r => r.GetAllAsync())
            .Returns(accountData.AsQueryable().BuildMock());
        accounts.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(accountData.AsQueryable().BuildMock());

        // Sprint 6.3 NOTI3-02 (#702) — biên nhận push.
        var receiptData = (pushReceiptSeed ?? Array.Empty<PushReceipt>()).ToList();
        var pushReceipts = new Mock<IGenericRepository<PushReceipt>>();
        pushReceipts.Setup(r => r.GetAllAsync())
            .Returns(() => receiptData.AsQueryable().BuildMock());
        pushReceipts.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(() => receiptData.AsQueryable().BuildMock());
        // AddAsync phải thật sự thêm vào tập dữ liệu, nếu không test "đã lưu receipt chưa" luôn rỗng.
        pushReceipts.Setup(r => r.AddAsync(It.IsAny<PushReceipt>()))
            .Callback<PushReceipt>(receiptData.Add)
            .Returns(Task.CompletedTask);

        // Sprint 6.3 NOTI3-04 (#704) — tuỳ chọn theo nhóm × kênh.
        var categoryPrefData = (categoryPreferenceSeed ?? Array.Empty<NotificationCategoryPreference>()).ToList();
        var categoryPrefs = new Mock<IGenericRepository<NotificationCategoryPreference>>();
        categoryPrefs.Setup(r => r.GetAllAsync())
            .Returns(() => categoryPrefData.AsQueryable().BuildMock());
        categoryPrefs.Setup(r => r.GetAllAsync(It.IsAny<bool>()))
            .Returns(() => categoryPrefData.AsQueryable().BuildMock());
        categoryPrefs.Setup(r => r.AddAsync(It.IsAny<NotificationCategoryPreference>()))
            .Callback<NotificationCategoryPreference>(categoryPrefData.Add)
            .Returns(Task.CompletedTask);

        var auditLogs = new Mock<IGenericRepository<NotificationAuditLog>>();
        var auditOutboxes = new Mock<IGenericRepository<NotificationAuditOutbox>>();

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.DeviceTokens).Returns(deviceTokens.Object);
        uow.SetupGet(u => u.Notifications).Returns(notifications.Object);
        uow.SetupGet(u => u.NotificationPreferences).Returns(preferences.Object);
        uow.SetupGet(u => u.NotificationTemplates).Returns(templates.Object);
        uow.SetupGet(u => u.Accounts).Returns(accounts.Object);
        uow.SetupGet(u => u.PushReceipts).Returns(pushReceipts.Object);
        uow.SetupGet(u => u.NotificationCategoryPreferences).Returns(categoryPrefs.Object);
        uow.SetupGet(u => u.NotificationAuditLogs).Returns(auditLogs.Object);
        uow.SetupGet(u => u.NotificationAuditOutboxes).Returns(auditOutboxes.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, deviceTokens, notifications);
    }
}
