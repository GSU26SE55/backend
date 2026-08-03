using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.Infrastructure.Persistence;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-11 (#711) — dọn notification quá hạn.
///
/// Bảng <c>notifications</c> chỉ tăng: mỗi sự kiện fan-out tối đa 4 dòng, sau vài tháng là hàng
/// triệu dòng và chính truy vấn feed của người dùng chậm theo.
/// </summary>
public class NotificationRetentionTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _provider;

    public NotificationRetentionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"retention-{Guid.NewGuid()}")
            .Options;

        _db = new ApplicationDbContext(options, null!);

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private NotificationRetentionBackgroundService Sut(NotificationRetentionOptions? options = null) =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(),
            new NoopCache(),
            Options.Create(options ?? new NotificationRetentionOptions { Days = 90 }),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationRetentionBackgroundService>.Instance);

    private NotificationEntity Add(
        int ageDays,
        NotificationStatusEnum status = NotificationStatusEnum.Read,
        NotificationTypeEnum type = NotificationTypeEnum.TicketCreated)
    {
        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = type,
            Channel = NotificationChannelEnum.InApp,
            Status = status,
            Title = "T",
            Body = "B",
            CreatedAt = DateTime.UtcNow.AddDays(-ageDays),
        };

        _db.Notifications.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    [Fact]
    public async Task OldReadNotification_IsSoftDeleted()
    {
        var old = Add(ageDays: 200);

        var removed = await Sut().PurgeAsync(CancellationToken.None);

        removed.Should().Be(1);

        var reloaded = await _db.Notifications.FirstAsync(n => n.Id == old.Id);
        reloaded.IsDeleted.Should().BeTrue();
        reloaded.DeletedAt.Should().NotBeNull();
    }

    /// <summary>Xoá mềm chứ không DELETE — ngưỡng cấu hình sai vẫn phục hồi được.</summary>
    [Fact]
    public async Task Purge_KeepsRowInDatabase()
    {
        Add(ageDays: 200);

        await Sut().PurgeAsync(CancellationToken.None);

        _db.Notifications.IgnoreQueryFilters().Count().Should().Be(1, "chỉ đánh dấu, không xoá thật");
    }

    [Fact]
    public async Task RecentNotification_IsUntouched()
    {
        var recent = Add(ageDays: 10);

        (await Sut().PurgeAsync(CancellationToken.None)).Should().Be(0);

        (await _db.Notifications.FirstAsync(n => n.Id == recent.Id)).IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// Bản Pending là thông báo CHƯA từng được gửi. Dọn nó đi là mất luôn nội dung nghiệp vụ,
    /// không phải dọn rác.
    /// </summary>
    [Fact]
    public async Task PendingNotification_IsNeverPurged_EvenWhenOld()
    {
        var pending = Add(ageDays: 500, NotificationStatusEnum.Pending);

        (await Sut().PurgeAsync(CancellationToken.None)).Should().Be(0);

        (await _db.Notifications.FirstAsync(n => n.Id == pending.Id)).IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// Notification critical là bằng chứng "đã cảnh báo" — cần cho điều tra sự cố và đối chiếu SLA.
    /// <c>SlaBreached</c> nằm trong <c>DefaultCriticalTypes</c>.
    /// </summary>
    [Fact]
    public async Task CriticalNotification_IsKeptForever()
    {
        var critical = Add(ageDays: 900, NotificationStatusEnum.Read, NotificationTypeEnum.SlaBreached);
        var normal = Add(ageDays: 900);

        (await Sut().PurgeAsync(CancellationToken.None)).Should().Be(1);

        (await _db.Notifications.FirstAsync(n => n.Id == critical.Id)).IsDeleted.Should().BeFalse();
        (await _db.Notifications.FirstAsync(n => n.Id == normal.Id)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task KeepCriticalForever_Disabled_PurgesCriticalToo()
    {
        Add(ageDays: 900, NotificationStatusEnum.Read, NotificationTypeEnum.SlaBreached);

        var sut = Sut(new NotificationRetentionOptions { Days = 90, KeepCriticalForever = false });

        (await sut.PurgeAsync(CancellationToken.None)).Should().Be(1);
    }

    [Theory]
    [InlineData(NotificationStatusEnum.Sent)]
    [InlineData(NotificationStatusEnum.Delivered)]
    [InlineData(NotificationStatusEnum.Read)]
    [InlineData(NotificationStatusEnum.Opened)]
    [InlineData(NotificationStatusEnum.Failed)]
    public async Task AllTerminalStatuses_ArePurgeable(NotificationStatusEnum status)
    {
        Add(ageDays: 200, status);

        (await Sut().PurgeAsync(CancellationToken.None)).Should().Be(1);
    }

    /// <summary>Batch nhỏ + nhiều vòng để không khoá bảng lâu và không đẩy khối WAL lớn sang replica.</summary>
    [Fact]
    public async Task Purge_ProcessesInBatches_UntilExhausted()
    {
        for (var i = 0; i < 12; i++)
            Add(ageDays: 200 + i);

        var sut = Sut(new NotificationRetentionOptions { Days = 90, BatchSize = 5, MaxBatchesPerRun = 20 });

        (await sut.PurgeAsync(CancellationToken.None)).Should().Be(12);
        (await _db.Notifications.CountAsync(n => !n.IsDeleted)).Should().Be(0);
    }

    /// <summary>Trần số vòng để một đợt tồn đọng lớn không giữ worker chạy vô hạn.</summary>
    [Fact]
    public async Task Purge_StopsAtMaxBatchesPerRun()
    {
        for (var i = 0; i < 20; i++)
            Add(ageDays: 200 + i);

        var sut = Sut(new NotificationRetentionOptions { Days = 90, BatchSize = 5, MaxBatchesPerRun = 2 });

        (await sut.PurgeAsync(CancellationToken.None)).Should().Be(10);
        (await _db.Notifications.CountAsync(n => !n.IsDeleted)).Should().Be(10, "phần còn lại dọn đêm sau");
    }

    /// <summary>Ngưỡng cấu hình phải có hiệu lực — không thì Days chỉ là số trang trí.</summary>
    [Fact]
    public async Task RetentionDays_IsRespected()
    {
        Add(ageDays: 40);
        Add(ageDays: 20);

        var sut = Sut(new NotificationRetentionOptions { Days = 30 });

        (await sut.PurgeAsync(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task AlreadyDeleted_IsNotCountedAgain()
    {
        var old = Add(ageDays: 200);
        old.IsDeleted = true;
        await _db.SaveChangesAsync();

        (await Sut().PurgeAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Disabled_ByConfiguration_DoesNothing()
    {
        var old = Add(ageDays: 500);

        var sut = Sut(new NotificationRetentionOptions { Enabled = false, Days = 90 });
        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        (await _db.Notifications.FirstAsync(n => n.Id == old.Id)).IsDeleted.Should().BeFalse();
    }

    private sealed class NoopCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => Task.CompletedTask;
    }
}
