using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// GH-672 NOTI-01 — kiểm tra trực tiếp một batch bằng EF InMemory để không phụ thuộc timer.
/// </summary>
public class NotificationDispatchBackgroundServiceTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, null!);
    }

    private static NotificationDispatchBackgroundService BuildSut(
        ApplicationDbContext db,
        INotificationDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(dispatcher);
        var provider = services.BuildServiceProvider();

        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        return new NotificationDispatchBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            cache.Object,
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);
    }

    private static Notification Seed(
        NotificationStatusEnum status,
        DateTime createdAt,
        bool isDeleted = false,
        NotificationChannelEnum channel = NotificationChannelEnum.InApp) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Channel = channel,
            Type = NotificationTypeEnum.System,
            Status = status,
            Title = "T",
            Body = "B",
            CreatedAt = createdAt,
            IsDeleted = isDeleted,
        };

    [Fact]
    public async Task ProcessBatch_DispatchesOnlyPendingRows_OldestFirst()
    {
        var now = DateTime.UtcNow;
        var oldest = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-10));
        var newest = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));
        var alreadySent = Seed(NotificationStatusEnum.Sent, now.AddMinutes(-5));
        var softDeleted = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-5), isDeleted: true);

        await using var db = NewDb(nameof(ProcessBatch_DispatchesOnlyPendingRows_OldestFirst));
        db.Notifications.AddRange(oldest, newest, alreadySent, softDeleted);
        await db.SaveChangesAsync();

        var dispatched = new ConcurrentQueue<Guid>();
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DispatchOutcome.Sent)
                  .Callback<Notification, CancellationToken>((n, _) => dispatched.Enqueue(n.Id));

        await BuildSut(db, dispatcher.Object).ProcessBatchAsync(CancellationToken.None);

        dispatched.Should().Equal(oldest.Id, newest.Id);
        dispatcher.Verify(d => d.DispatchPendingAsync(
            It.Is<Notification>(n => n.Id == alreadySent.Id || n.Id == softDeleted.Id),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Sau #673, Email phải được xử lý cùng các channel khác và vẫn giữ đúng thứ tự cũ nhất trước.
    /// </summary>
    [Fact]
    public async Task ProcessBatch_EmailRowsAreDispatchedWithOtherChannels_OldestFirst()
    {
        var now = DateTime.UtcNow;
        var oldEmail1 = Seed(NotificationStatusEnum.Pending, now.AddHours(-3), channel: NotificationChannelEnum.Email);
        var oldEmail2 = Seed(NotificationStatusEnum.Pending, now.AddHours(-2), channel: NotificationChannelEnum.Email);
        var newerInApp = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));

        await using var db = NewDb(nameof(ProcessBatch_EmailRowsAreDispatchedWithOtherChannels_OldestFirst));
        db.Notifications.AddRange(oldEmail1, oldEmail2, newerInApp);
        await db.SaveChangesAsync();

        var dispatched = new ConcurrentQueue<Guid>();
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DispatchOutcome.Sent)
                  .Callback<Notification, CancellationToken>((n, _) => dispatched.Enqueue(n.Id));

        await BuildSut(db, dispatcher.Object).ProcessBatchAsync(CancellationToken.None);

        dispatched.Should().Equal(oldEmail1.Id, oldEmail2.Id, newerInApp.Id);
    }

    [Fact]
    public async Task ProcessBatch_OneRowThrows_RemainingRowsStillDispatched()
    {
        var now = DateTime.UtcNow;
        var failing = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-10));
        var healthy = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));

        await using var db = NewDb(nameof(ProcessBatch_OneRowThrows_RemainingRowsStillDispatched));
        db.Notifications.AddRange(failing, healthy);
        await db.SaveChangesAsync();

        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(
                      It.Is<Notification>(n => n.Id == failing.Id), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("channel exploded"));
        dispatcher.Setup(d => d.DispatchPendingAsync(
                      It.Is<Notification>(n => n.Id == healthy.Id), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DispatchOutcome.Sent);

        await BuildSut(db, dispatcher.Object).ProcessBatchAsync(CancellationToken.None);

        dispatcher.Verify(d => d.DispatchPendingAsync(
            It.Is<Notification>(n => n.Id == healthy.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
