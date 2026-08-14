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

using NotificationService.UnitTests.Helpers;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// GH-792 — vòng đời của một bản ghi đã CHIẾM (<c>Processing</c>).
/// </summary>
/// <remarks>
/// <para>
/// Chiếm việc trước khi gọi provider loại bỏ được lần gửi trùng, nhưng đẻ ra một rủi ro mới: tiến
/// trình chết giữa chừng thì bản ghi nằm mãi ở <c>Processing</c>, không khớp bộ lọc <c>Pending</c>
/// nên không vòng quét nào nhặt — người dùng không nhận được thông báo, mà chẳng có lỗi nào nổi lên.
/// </para>
/// <para>
/// Hai khẳng định phải cùng đúng thì thiết kế mới đứng vững:
/// bản ghi vừa chiếm KHÔNG bị đụng vào (nếu không, hai tiến trình cùng gửi một thông báo), còn bản
/// ghi chiếm quá lâu thì PHẢI được trả về hàng đợi.
/// </para>
/// </remarks>
public class StaleClaimReclaimTests
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
        INotificationDispatcher dispatcher,
        NotificationDispatchOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(dispatcher);
        var provider = services.BuildServiceProvider();

        return new NotificationDispatchBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryLease(),
            Options.Create(options ?? new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);
    }

    private static Notification Claimed(DateTime claimedAt, int attempts = 1) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Channel = NotificationChannelEnum.Email,
        Type = NotificationTypeEnum.System,
        Status = NotificationStatusEnum.Processing,
        Title = "T",
        Body = "B",
        CreatedAt = claimedAt,
        ProcessingStartedAt = claimedAt,
        DispatchAttemptCount = attempts,
    };

    private static Mock<INotificationDispatcher> SpyDispatcher()
    {
        var m = new Mock<INotificationDispatcher>();
        m.Setup(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(DispatchOutcome.Sent);
        return m;
    }

    [Fact]
    public async Task FreshlyClaimedRecord_IsNotDispatchedAgain()
    {
        // ĐÂY là điều kiện chống gửi trùng: dấu vết mà một tiến trình vừa chết để lại không được
        // biến thành lần gửi thứ hai ngay lập tức.
        await using var db = NewDb(nameof(FreshlyClaimedRecord_IsNotDispatchedAgain));
        db.Notifications.Add(Claimed(DateTime.UtcNow));
        await db.SaveChangesAsync();

        var dispatcher = SpyDispatcher();
        var result = await BuildSut(db, dispatcher.Object).ProcessBatchAsync(CancellationToken.None);

        result.Reclaimed.Should().Be(0);
        dispatcher.Verify(d => d.DispatchPendingAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()),
            Times.Never, "bản ghi đang được gửi không được ai đụng vào");
    }

    [Fact]
    public async Task StaleClaim_IsReturnedToTheQueue()
    {
        await using var db = NewDb(nameof(StaleClaim_IsReturnedToTheQueue));
        var stuck = Claimed(DateTime.UtcNow.AddMinutes(-30));
        db.Notifications.Add(stuck);
        await db.SaveChangesAsync();

        var result = await BuildSut(db, SpyDispatcher().Object).ProcessBatchAsync(CancellationToken.None);

        result.Reclaimed.Should().Be(1);

        var row = await db.Notifications.SingleAsync(n => n.Id == stuck.Id);
        row.Status.Should().Be(NotificationStatusEnum.Pending);
        row.ProcessingStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task ReclaimedRecord_KeepsItsAttemptCount()
    {
        // Đặt lại số đếm về 0 sẽ biến một sự cố lặp lại thành vòng lặp vô tận: mỗi lần chết là mỗi
        // lần làm lại từ đầu, MaxAttempts không bao giờ chạm tới.
        await using var db = NewDb(nameof(ReclaimedRecord_KeepsItsAttemptCount));
        var stuck = Claimed(DateTime.UtcNow.AddMinutes(-30), attempts: 3);
        db.Notifications.Add(stuck);
        await db.SaveChangesAsync();

        await BuildSut(db, SpyDispatcher().Object).ProcessBatchAsync(CancellationToken.None);

        (await db.Notifications.SingleAsync(n => n.Id == stuck.Id))
            .DispatchAttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ReclaimedRecord_IsDispatchedOnTheSamePass()
    {
        // Thu hồi rồi phải gửi ngay trong vòng này, không đợi thêm một chu kỳ nữa: thông báo đã trễ
        // sẵn vì sự cố, kéo dài thêm chỉ làm người dùng chờ lâu hơn.
        await using var db = NewDb(nameof(ReclaimedRecord_IsDispatchedOnTheSamePass));
        var stuck = Claimed(DateTime.UtcNow.AddMinutes(-30));
        db.Notifications.Add(stuck);
        await db.SaveChangesAsync();

        var dispatcher = SpyDispatcher();
        await BuildSut(db, dispatcher.Object).ProcessBatchAsync(CancellationToken.None);

        dispatcher.Verify(d => d.DispatchPendingAsync(
            It.Is<Notification>(n => n.Id == stuck.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SoftDeletedClaim_IsLeftAlone()
    {
        // Bản ghi đã xoá mềm không được sống lại chỉ vì nó kẹt ở Processing.
        await using var db = NewDb(nameof(SoftDeletedClaim_IsLeftAlone));
        var deleted = Claimed(DateTime.UtcNow.AddMinutes(-30));
        deleted.IsDeleted = true;
        db.Notifications.Add(deleted);
        await db.SaveChangesAsync();

        var result = await BuildSut(db, SpyDispatcher().Object).ProcessBatchAsync(CancellationToken.None);

        result.Reclaimed.Should().Be(0);
        (await db.Notifications.SingleAsync(n => n.Id == deleted.Id))
            .Status.Should().Be(NotificationStatusEnum.Processing);
    }

    [Fact]
    public async Task ClaimWithoutTimestamp_IsReclaimed()
    {
        // Bản ghi từ trước khi có cột mốc thời gian (hoặc bị ghi thiếu) mà không thu hồi được thì
        // sẽ kẹt vĩnh viễn — đúng cái triệu chứng mà thay đổi này ra đời để xoá bỏ.
        await using var db = NewDb(nameof(ClaimWithoutTimestamp_IsReclaimed));
        var orphan = Claimed(DateTime.UtcNow.AddMinutes(-30));
        orphan.ProcessingStartedAt = null;
        db.Notifications.Add(orphan);
        await db.SaveChangesAsync();

        var result = await BuildSut(db, SpyDispatcher().Object).ProcessBatchAsync(CancellationToken.None);

        result.Reclaimed.Should().Be(1);
        (await db.Notifications.SingleAsync(n => n.Id == orphan.Id))
            .Status.Should().Be(NotificationStatusEnum.Pending);
    }

    [Fact]
    public async Task ReclaimTimeout_HasAFloor_SoAConfigTypoCannotCauseDoubleSends()
    {
        // Đặt ProcessingTimeoutSeconds = 0 (hoặc âm) sẽ thu hồi ngay bản ghi ĐANG gửi, và lúc đó hai
        // tiến trình cùng gửi một thông báo — chính điều mà thay đổi này tồn tại để ngăn.
        await using var db = NewDb(nameof(ReclaimTimeout_HasAFloor_SoAConfigTypoCannotCauseDoubleSends));
        db.Notifications.Add(Claimed(DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await BuildSut(db, SpyDispatcher().Object,
                new NotificationDispatchOptions { ProcessingTimeoutSeconds = 0 })
            .ProcessBatchAsync(CancellationToken.None);

        result.Reclaimed.Should().Be(0);
    }
}
