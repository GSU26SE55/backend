using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.UnitTests.Helpers;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// GH-672 NOTI-01 — worker poll 5s nên mỗi test phải chờ đúng 1 tick.
/// Dùng TaskCompletionSource thay vì Task.Delay cố định để test kết thúc ngay khi tick chạy xong.
/// </summary>
public class NotificationDispatchBackgroundServiceTests
{
    private static readonly TimeSpan TickTimeout = TimeSpan.FromSeconds(20);

    private static NotificationDispatchBackgroundService BuildSut(
        INotificationUnitOfWork unitOfWork,
        INotificationDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => unitOfWork);
        services.AddScoped(_ => dispatcher);
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

    private static async Task RunOneTickAsync(NotificationDispatchBackgroundService sut, Task processed)
    {
        await sut.StartAsync(CancellationToken.None);
        try
        {
            var finished = await Task.WhenAny(processed, Task.Delay(TickTimeout));
            finished.Should().Be(processed, "worker phải xử lý batch trong vòng 1 tick");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Tick_DispatchesOnlyPendingRows_OldestFirst()
    {
        var now = DateTime.UtcNow;
        var oldest = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-10));
        var newest = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));
        var alreadySent = Seed(NotificationStatusEnum.Sent, now.AddMinutes(-5));
        var softDeleted = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-5), isDeleted: true);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [oldest, newest, alreadySent, softDeleted]);

        var dispatched = new ConcurrentQueue<Guid>();
        var processed = new TaskCompletionSource();
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true)
                  .Callback<Notification, CancellationToken>((n, _) =>
                  {
                      dispatched.Enqueue(n.Id);
                      if (dispatched.Count == 2)
                          processed.TrySetResult();
                  });

        await RunOneTickAsync(BuildSut(uow.Object, dispatcher.Object), processed.Task);

        dispatched.Should().Equal(oldest.Id, newest.Id);
        dispatcher.Verify(d => d.DispatchPendingAsync(
            It.Is<Notification>(n => n.Id == alreadySent.Id || n.Id == softDeleted.Id),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Regression: row Email luôn bị hoãn tới khi #673 merge. Nếu chúng lọt vào batch,
    /// chỉ cần tích đủ BatchSize row Email là chiếm sạch chỗ và row Push/InApp mới không
    /// bao giờ tới lượt — worker chết im lặng.
    /// </summary>
    [Fact]
    public async Task Tick_EmailRowsExcludedFromBatch_DoNotStarveOtherChannels()
    {
        var now = DateTime.UtcNow;
        // Email cũ hơn → nếu không loại khỏi query sẽ đứng đầu OrderBy(CreatedAt).
        var oldEmail1 = Seed(NotificationStatusEnum.Pending, now.AddHours(-3), channel: NotificationChannelEnum.Email);
        var oldEmail2 = Seed(NotificationStatusEnum.Pending, now.AddHours(-2), channel: NotificationChannelEnum.Email);
        var newerInApp = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [oldEmail1, oldEmail2, newerInApp]);

        var dispatched = new ConcurrentQueue<Guid>();
        var processed = new TaskCompletionSource();
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true)
                  .Callback<Notification, CancellationToken>((n, _) =>
                  {
                      dispatched.Enqueue(n.Id);
                      processed.TrySetResult();
                  });

        await RunOneTickAsync(BuildSut(uow.Object, dispatcher.Object), processed.Task);

        dispatched.Should().Equal(newerInApp.Id);
    }

    [Fact]
    public async Task Tick_OneRowThrows_RemainingRowsStillDispatched()
    {
        var now = DateTime.UtcNow;
        var failing = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-10));
        var healthy = Seed(NotificationStatusEnum.Pending, now.AddMinutes(-1));

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: [failing, healthy]);

        var processed = new TaskCompletionSource();
        var dispatcher = new Mock<INotificationDispatcher>();
        dispatcher.Setup(d => d.DispatchPendingAsync(
                      It.Is<Notification>(n => n.Id == failing.Id), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("channel exploded"));
        dispatcher.Setup(d => d.DispatchPendingAsync(
                      It.Is<Notification>(n => n.Id == healthy.Id), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true)
                  .Callback(() => processed.TrySetResult());

        await RunOneTickAsync(BuildSut(uow.Object, dispatcher.Object), processed.Task);

        dispatcher.Verify(d => d.DispatchPendingAsync(
            It.Is<Notification>(n => n.Id == healthy.Id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
