using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.Infrastructure.Persistence;
using NotificationService.UnitTests.Helpers;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// GH-793 — quyền chạy phải được gia hạn GIỮA một lượt dài, và mất quyền thì phải dừng.
/// </summary>
/// <remarks>
/// Thời hạn quyền là 30 giây, còn một lượt có thể xử lý tới 100 bản ghi với từng ấy lần gọi ra
/// ngoài (Expo, Mailjet, SMS gateway). Không gia hạn thì quyền hết hạn giữa chừng, instance khác
/// giành được, và hai bên cùng chạy trên cùng một hàng đợi — đúng cái cảnh mà quyền độc quyền sinh
/// ra để tránh.
/// </remarks>
public class LeaseRenewalDuringBatchTests
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
        ApplicationDbContext db, INotificationDispatcher dispatcher, InMemoryLease lease)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(dispatcher);
        var provider = services.BuildServiceProvider();

        return new NotificationDispatchBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lease,
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);
    }

    private static Notification PendingRow(DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Channel = NotificationChannelEnum.InApp,
        Type = NotificationTypeEnum.System,
        Status = NotificationStatusEnum.Pending,
        Title = "T",
        Body = "B",
        CreatedAt = createdAt,
    };

    private static async Task<ApplicationDbContext> SeedAsync(string name, int count)
    {
        var db = NewDb(name);
        var baseTime = DateTime.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
            db.Notifications.Add(PendingRow(baseTime.AddSeconds(i)));
        await db.SaveChangesAsync();
        return db;
    }

    private static Mock<INotificationDispatcher> CountingDispatcher(Action onDispatch)
    {
        var m = new Mock<INotificationDispatcher>();
        m.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
         .Callback(onDispatch)
         .ReturnsAsync(DispatchOutcome.Sent);
        return m;
    }

    [Fact]
    public async Task LongBatch_RenewsTheLeaseAlongTheWay()
    {
        await using var db = await SeedAsync(nameof(LongBatch_RenewsTheLeaseAlongTheWay), 30);
        var lease = new InMemoryLease();
        await lease.TryAcquireAsync("notification_dispatch_leader", "me", TimeSpan.FromSeconds(30));

        var sut = BuildSut(db, CountingDispatcher(() => { }).Object, lease);

        await sut.ProcessBatchAsync(CancellationToken.None,
            ct => lease.TryRenewAsync("notification_dispatch_leader", "me", TimeSpan.FromSeconds(30), ct));

        lease.RenewCalls.Should().BeGreaterThan(0,
            "lượt dài mà không gia hạn thì quyền hết hạn giữa chừng và instance khác chen vào");
    }

    [Fact]
    public async Task LosingTheLeaseMidBatch_StopsTheRun()
    {
        // Chạy tiếp sau khi đã mất quyền nghĩa là hai instance cùng làm việc trên một hàng đợi.
        await using var db = await SeedAsync(nameof(LosingTheLeaseMidBatch_StopsTheRun), 40);
        var lease = new InMemoryLease { RenewFails = true };
        var dispatched = 0;

        var sut = BuildSut(db, CountingDispatcher(() => dispatched++).Object, lease);

        await sut.ProcessBatchAsync(CancellationToken.None,
            ct => lease.TryRenewAsync("notification_dispatch_leader", "me", TimeSpan.FromSeconds(30), ct));

        dispatched.Should().BeLessThan(40, "phải dừng lại ở lần gia hạn thất bại đầu tiên");
        dispatched.Should().BeGreaterThan(0, "phần đã làm trước khi mất quyền vẫn hợp lệ");
    }

    [Fact]
    public async Task WithoutARenewCallback_TheBatchStillCompletes()
    {
        // Đường gọi từ test và các job chạy một mình không truyền hàm gia hạn; thiếu nó không được
        // biến thành dừng sớm.
        await using var db = await SeedAsync(nameof(WithoutARenewCallback_TheBatchStillCompletes), 25);
        var dispatched = 0;

        var sut = BuildSut(db, CountingDispatcher(() => dispatched++).Object, new InMemoryLease());

        await sut.ProcessBatchAsync(CancellationToken.None);

        dispatched.Should().Be(25);
    }

    [Fact]
    public async Task ShortBatch_DoesNotSpendRoundTripsRenewing()
    {
        // Gia hạn ở mỗi bản ghi là một vòng tới Redis cho mỗi thông báo — tự biến mình thành nguồn
        // chậm. Lượt ngắn thì không cần gia hạn lần nào.
        await using var db = await SeedAsync(nameof(ShortBatch_DoesNotSpendRoundTripsRenewing), 5);
        var lease = new InMemoryLease();

        var sut = BuildSut(db, CountingDispatcher(() => { }).Object, lease);

        await sut.ProcessBatchAsync(CancellationToken.None,
            ct => lease.TryRenewAsync("notification_dispatch_leader", "me", TimeSpan.FromSeconds(30), ct));

        lease.RenewCalls.Should().Be(0);
    }
}
