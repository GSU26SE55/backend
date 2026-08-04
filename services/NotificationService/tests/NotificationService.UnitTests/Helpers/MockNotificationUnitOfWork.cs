using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedKernels.Domain;
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
            IEnumerable<NotificationCategoryPreference>? categoryPreferenceSeed = null,
            // Sprint 6.4 — thêm ở CUỐI và đều tuỳ chọn, nên mọi lời gọi cũ vẫn hợp lệ nguyên vẹn.
            IEnumerable<NotificationGroup>? groupSeed = null,
            IEnumerable<NotificationGroupMember>? groupMemberSeed = null,
            IEnumerable<NotificationBatch>? batchSeed = null,
            IEnumerable<NotificationBatchTarget>? batchTargetSeed = null)
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

        // Sprint 6.4 — nhóm người nhận và lần gửi. AddAsync phải THẬT SỰ thêm vào tập dữ liệu,
        // nếu không test kiểu "đã tạo đúng mấy dòng chưa" luôn thấy rỗng.
        var groups = BuildRepo(groupSeed);
        var groupMembers = BuildRepo(groupMemberSeed);
        var batches = BuildRepo(batchSeed);
        var batchTargets = BuildRepo(batchTargetSeed);

        var uow = new Mock<INotificationUnitOfWork>();
        uow.SetupGet(u => u.NotificationGroups).Returns(groups.Object);
        uow.SetupGet(u => u.NotificationGroupMembers).Returns(groupMembers.Object);
        uow.SetupGet(u => u.NotificationBatches).Returns(batches.Object);
        uow.SetupGet(u => u.NotificationBatchTargets).Returns(batchTargets.Object);
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

    /// <summary>
    /// Sprint 6.4 — mock repository có trạng thái: <c>AddAsync</c> thêm thật, <c>DeleteAsync</c> đánh
    /// dấu xoá mềm (giống <c>AuditableEntityInterceptor</c> ngoài đời), <c>GetAllAsync</c> đọc lại từ
    /// chính tập đó. Nhờ vậy test khẳng định được kết quả cuối thay vì chỉ đếm số lần gọi.
    /// </summary>
    private static Mock<IGenericRepository<T>> BuildRepo<T>(IEnumerable<T>? seed) where T : AuditableEntity
    {
        var data = (seed ?? Array.Empty<T>()).ToList();
        var repo = new Mock<IGenericRepository<T>>();

        repo.Setup(r => r.GetAllAsync()).Returns(() => data.AsQueryable().BuildMock());
        repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(() => data.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<T>())).Callback<T>(data.Add).Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteAsync(It.IsAny<T>())).Callback<T>(e =>
        {
            e.IsDeleted = true;
            e.DeletedAt = DateTime.UtcNow;
        });

        return repo;
    }
}
