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
/// Sprint 6.2 NOTI-01 (#672) + NOTI-12 (#683) — hai worker nền: quét record Pending để giao,
/// và gom digest. Dùng EF InMemory vì cả hai truy cập <see cref="ApplicationDbContext"/> trực tiếp.
/// </summary>
public class NotificationWorkerTests
{
    // ── hạ tầng test ─────────────────────────────────────────────────────────

    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, null!);
    }

    /// <summary>Cache phân tán giả — luôn cho instance hiện tại làm leader.</summary>

    private sealed class StubDispatcher : INotificationDispatcher
    {
        private readonly Func<Notification, DispatchOutcome> _behaviour;
        public List<Guid> Dispatched { get; } = new();

        public StubDispatcher(Func<Notification, DispatchOutcome>? behaviour = null)
            => _behaviour = behaviour ?? (_ => DispatchOutcome.Sent);

        public Task DispatchAsync(
            Application.DTOs.Request.Notification.DispatchRequest request, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<DispatchOutcome> DispatchPendingAsync(Notification notification, CancellationToken ct = default)
        {
            Dispatched.Add(notification.Id);
            var outcome = _behaviour(notification);
            if (outcome == DispatchOutcome.Sent)
            {
                notification.Status = NotificationStatusEnum.Sent;
                notification.SentAt = DateTime.UtcNow;
            }
            return Task.FromResult(outcome);
        }
    }

    private static IServiceScopeFactory ScopeFactory(ApplicationDbContext db, INotificationDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(dispatcher);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static Notification Pending(
        Guid userId,
        NotificationChannelEnum channel = NotificationChannelEnum.InApp,
        DateTime? nextAttemptAt = null,
        int attempts = 0,
        string? entityType = "Ticket",
        DateTime? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = NotificationTypeEnum.TicketCreated,
            Channel = channel,
            Status = NotificationStatusEnum.Pending,
            Title = "T",
            Body = "B",
            EntityType = entityType,
            NextAttemptAt = nextAttemptAt,
            DispatchAttemptCount = attempts,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };

    // ── NOTI-01: dispatch worker ─────────────────────────────────────────────

    [Fact]
    public async Task ProcessBatch_DispatchesDuePendingRecords()
    {
        await using var db = NewDb(nameof(ProcessBatch_DispatchesDuePendingRecords));
        var userId = Guid.NewGuid();
        db.Notifications.AddRange(Pending(userId), Pending(userId));
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher();
        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        var result = await sut.ProcessBatchAsync(CancellationToken.None);

        result.Sent.Should().Be(2);
        result.Total.Should().Be(2);
        dispatcher.Dispatched.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessBatch_SkipsRecordsScheduledForLater()
    {
        await using var db = NewDb(nameof(ProcessBatch_SkipsRecordsScheduledForLater));
        var userId = Guid.NewGuid();
        db.Notifications.Add(Pending(userId, nextAttemptAt: DateTime.UtcNow.AddMinutes(30)));
        db.Notifications.Add(Pending(userId, nextAttemptAt: DateTime.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher();
        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        var result = await sut.ProcessBatchAsync(CancellationToken.None);

        result.Total.Should().Be(1, "record hẹn 30 phút nữa chưa tới hạn");
    }

    [Fact]
    public async Task ProcessBatch_SkipsRecordsThatExhaustedAttempts()
    {
        await using var db = NewDb(nameof(ProcessBatch_SkipsRecordsThatExhaustedAttempts));
        db.Notifications.Add(Pending(Guid.NewGuid(), attempts: 5));
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher();
        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions { MaxAttempts = 5 }),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        (await sut.ProcessBatchAsync(CancellationToken.None)).Total.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_SkipsSoftDeletedAndNonPending()
    {
        await using var db = NewDb(nameof(ProcessBatch_SkipsSoftDeletedAndNonPending));
        var userId = Guid.NewGuid();

        var deleted = Pending(userId);
        deleted.IsDeleted = true;
        var sent = Pending(userId);
        sent.Status = NotificationStatusEnum.Sent;

        db.Notifications.AddRange(deleted, sent);
        await db.SaveChangesAsync();

        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, new StubDispatcher()), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        (await sut.ProcessBatchAsync(CancellationToken.None)).Total.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatch_RespectsBatchSize_AndOldestFirst()
    {
        await using var db = NewDb(nameof(ProcessBatch_RespectsBatchSize_AndOldestFirst));
        var userId = Guid.NewGuid();
        var oldest = Pending(userId, createdAt: DateTime.UtcNow.AddHours(-3));
        db.Notifications.AddRange(
            Pending(userId, createdAt: DateTime.UtcNow.AddHours(-1)),
            oldest,
            Pending(userId, createdAt: DateTime.UtcNow));
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher();
        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions { BatchSize = 1 }),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        await sut.ProcessBatchAsync(CancellationToken.None);

        dispatcher.Dispatched.Should().ContainSingle().And.Contain(oldest.Id);
    }

    /// <summary>1 record lỗi bất ngờ không được làm chết cả batch.</summary>
    [Fact]
    public async Task ProcessBatch_WhenDispatcherThrows_ContinuesWithOtherRecords()
    {
        await using var db = NewDb(nameof(ProcessBatch_WhenDispatcherThrows_ContinuesWithOtherRecords));
        var userId = Guid.NewGuid();
        var bad = Pending(userId, createdAt: DateTime.UtcNow.AddMinutes(-5));
        var good = Pending(userId, createdAt: DateTime.UtcNow);
        db.Notifications.AddRange(bad, good);
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher(n =>
            n.Id == bad.Id ? throw new InvalidOperationException("boom") : DispatchOutcome.Sent);

        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        var result = await sut.ProcessBatchAsync(CancellationToken.None);

        result.Errored.Should().Be(1);
        result.Sent.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_CountsEachOutcomeSeparately()
    {
        await using var db = NewDb(nameof(ProcessBatch_CountsEachOutcomeSeparately));
        var userId = Guid.NewGuid();
        var a = Pending(userId, createdAt: DateTime.UtcNow.AddMinutes(-3));
        var b = Pending(userId, createdAt: DateTime.UtcNow.AddMinutes(-2));
        var c = Pending(userId, createdAt: DateTime.UtcNow.AddMinutes(-1));
        db.Notifications.AddRange(a, b, c);
        await db.SaveChangesAsync();

        var dispatcher = new StubDispatcher(n =>
            n.Id == a.Id ? DispatchOutcome.Sent
            : n.Id == b.Id ? DispatchOutcome.Deferred
            : DispatchOutcome.Failed);

        var sut = new NotificationDispatchBackgroundService(
            ScopeFactory(db, dispatcher), new InMemoryLease(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatchBackgroundService>.Instance);

        var result = await sut.ProcessBatchAsync(CancellationToken.None);

        result.Sent.Should().Be(1);
        result.Deferred.Should().Be(1);
        result.Failed.Should().Be(1);
    }

    // ── NOTI-12: digest worker ───────────────────────────────────────────────

    private static NotificationDigestBackgroundService DigestSut(ApplicationDbContext db, NotificationDigestOptions? opts = null) =>
        new(ScopeFactory(db, new StubDispatcher()), new InMemoryLease(),
            Options.Create(opts ?? new NotificationDigestOptions()),
            NullLogger<NotificationDigestBackgroundService>.Instance);

    [Fact]
    public async Task BuildDigests_GroupsDueRecordsIntoOneAggregate_AndMarksOriginalsSent()
    {
        await using var db = NewDb(nameof(BuildDigests_GroupsDueRecordsIntoOneAggregate_AndMarksOriginalsSent));
        var userId = Guid.NewGuid();

        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EmailEnabled = true,
            DigestWindowMinutes = 15
        });

        var due = DateTime.UtcNow.AddMinutes(-1);
        var n1 = Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: due);
        var n2 = Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: due);
        db.Notifications.AddRange(n1, n2);
        await db.SaveChangesAsync();

        var created = await DigestSut(db).BuildDigestsAsync(CancellationToken.None);

        created.Should().Be(1);

        var aggregate = await db.Notifications.SingleAsync(n => n.EntityType == NotificationDigest.EntityType);
        aggregate.UserId.Should().Be(userId);
        aggregate.Channel.Should().Be(NotificationChannelEnum.Email);
        aggregate.Status.Should().Be(NotificationStatusEnum.Pending);
        aggregate.NextAttemptAt.Should().BeNull("bản tổng hợp phải được gửi ngay ở vòng dispatch kế tiếp");
        aggregate.Title.Should().Contain("2 thông báo");

        (await db.Notifications.Where(n => n.Id == n1.Id || n.Id == n2.Id).ToListAsync())
            .Should().AllSatisfy(n => n.Status.Should().Be(NotificationStatusEnum.Sent));
    }

    [Fact]
    public async Task BuildDigests_SeparatesByChannel()
    {
        await using var db = NewDb(nameof(BuildDigests_SeparatesByChannel));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });

        var due = DateTime.UtcNow.AddMinutes(-1);
        db.Notifications.AddRange(
            Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: due),
            Pending(userId, NotificationChannelEnum.Push, nextAttemptAt: due));
        await db.SaveChangesAsync();

        (await DigestSut(db).BuildDigestsAsync(CancellationToken.None)).Should().Be(2);
    }

    /// <summary>Record hoãn vì backoff lỗi (user KHÔNG bật digest) phải để dispatch worker thử lại.</summary>
    [Fact]
    public async Task BuildDigests_IgnoresUsersWithoutDigestPreference()
    {
        await using var db = NewDb(nameof(BuildDigests_IgnoresUsersWithoutDigestPreference));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EmailEnabled = true   // Immediate, không digest
        });
        db.Notifications.Add(Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: DateTime.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        (await DigestSut(db).BuildDigestsAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task BuildDigests_IgnoresRecordsNotYetDue()
    {
        await using var db = NewDb(nameof(BuildDigests_IgnoresRecordsNotYetDue));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });
        db.Notifications.Add(Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: DateTime.UtcNow.AddMinutes(10)));
        await db.SaveChangesAsync();

        (await DigestSut(db).BuildDigestsAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task BuildDigests_DoesNotReDigestAggregateRecords()
    {
        await using var db = NewDb(nameof(BuildDigests_DoesNotReDigestAggregateRecords));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });
        db.Notifications.Add(Pending(userId, NotificationChannelEnum.Email,
            nextAttemptAt: DateTime.UtcNow.AddMinutes(-1), entityType: NotificationDigest.EntityType));
        await db.SaveChangesAsync();

        (await DigestSut(db).BuildDigestsAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task BuildDigests_SingleItem_KeepsOriginalTitle()
    {
        await using var db = NewDb(nameof(BuildDigests_SingleItem_KeepsOriginalTitle));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Frequency = NotificationFrequencyEnum.Daily
        });

        var only = Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: DateTime.UtcNow.AddMinutes(-1));
        only.Title = "Chỉ một thông báo";
        db.Notifications.Add(only);
        await db.SaveChangesAsync();

        await DigestSut(db).BuildDigestsAsync(CancellationToken.None);

        var aggregate = await db.Notifications.SingleAsync(n => n.EntityType == NotificationDigest.EntityType);
        aggregate.Title.Should().Be("Chỉ một thông báo");
    }

    [Fact]
    public async Task BuildDigests_TruncatesBodyBeyondMaxItems()
    {
        await using var db = NewDb(nameof(BuildDigests_TruncatesBodyBeyondMaxItems));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });

        var due = DateTime.UtcNow.AddMinutes(-1);
        for (var i = 0; i < 5; i++)
            db.Notifications.Add(Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: due));
        await db.SaveChangesAsync();

        await DigestSut(db, new NotificationDigestOptions { MaxItemsInBody = 2 })
            .BuildDigestsAsync(CancellationToken.None);

        var aggregate = await db.Notifications.SingleAsync(n => n.EntityType == NotificationDigest.EntityType);
        aggregate.Body.Should().Contain("và 3 thông báo khác");
    }

    [Fact]
    public async Task BuildDigests_NoDueRecords_ReturnsZero()
    {
        await using var db = NewDb(nameof(BuildDigests_NoDueRecords_ReturnsZero));
        (await DigestSut(db).BuildDigestsAsync(CancellationToken.None)).Should().Be(0);
    }

    /// <summary>
    /// 03/08/2026 — bản tin gom KHÔNG được vượt giới hạn cột.
    ///
    /// <para>Cột <c>body</c> chỉ chứa 2000 ký tự, mà mỗi mục con cũng được phép dài tới 2000. Chỉ
    /// cần hai mục dài là bản gom vượt trần, Postgres ném lỗi và hỏng cả vòng gom. Trước thay đổi
    /// này <c>BuildBody</c> nối thẳng không cắt gì — chưa nổ chỉ vì hệ thống chưa sinh bản tin gom
    /// nào, tức là bẫy chờ sẵn chứ không phải chuyện không xảy ra.</para>
    /// </summary>
    [Fact]
    public async Task BuildDigests_MucConRatDai_ThanVanNamTrongGioiHanCot()
    {
        await using var db = NewDb(nameof(BuildDigests_MucConRatDai_ThanVanNamTrongGioiHanCot));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });

        var due = DateTime.UtcNow.AddMinutes(-1);
        for (var i = 0; i < 5; i++)
        {
            var n = Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: due);
            n.Title = new string('T', 200);    // đúng trần cột title
            n.Body = new string('B', 2000);    // đúng trần cột body
            db.Notifications.Add(n);
        }
        await db.SaveChangesAsync();

        await DigestSut(db, new NotificationDigestOptions { MaxItemsInBody = 5 })
            .BuildDigestsAsync(CancellationToken.None);

        var aggregate = await db.Notifications.SingleAsync(n => n.EntityType == NotificationDigest.EntityType);

        aggregate.Body.Length.Should().BeLessThanOrEqualTo(2000, "vượt là Postgres ném lỗi");
        aggregate.Title.Length.Should().BeLessThanOrEqualTo(200);
        aggregate.Body.Should().Contain("thông báo khác",
            "phần bị lược phải được nói ra, không im lặng nuốt mất");
    }

    /// <summary>Một mục con duy nhất mà dài quá trần cũng phải bị cắt.</summary>
    [Fact]
    public async Task BuildDigests_MotMucDuyNhatQuaDai_VanBiCat()
    {
        await using var db = NewDb(nameof(BuildDigests_MotMucDuyNhatQuaDai_VanBiCat));
        var userId = Guid.NewGuid();
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DigestWindowMinutes = 15
        });

        var only = Pending(userId, NotificationChannelEnum.Email, nextAttemptAt: DateTime.UtcNow.AddMinutes(-1));
        only.Body = new string('B', 2000);
        db.Notifications.Add(only);
        await db.SaveChangesAsync();

        await DigestSut(db).BuildDigestsAsync(CancellationToken.None);

        var aggregate = await db.Notifications.SingleAsync(n => n.EntityType == NotificationDigest.EntityType);
        aggregate.Body.Length.Should().BeLessThanOrEqualTo(2000);
    }
}
