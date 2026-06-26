using System.Text.Json;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.BackgroundJobs;
using AuthService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Infrastructure.Outbox;

internal class NullUserService : ICurrentUserService
{
    public string? UserId => null;
}

/// <summary>
/// OutboxRelayBackgroundService.ProcessBatchAsync là private. Tests gọi qua reflection
/// để verify logic batch + retry mà không cần chạy background loop thật.
/// </summary>
public class OutboxRelayBackgroundServiceTests
{
    private static (ApplicationDbContext db, IServiceProvider sp, Mock<IPublishEndpoint> publishMock)
        BuildScopedDeps(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var interceptor = new AuditableEntityInterceptor(new NullUserService());
        var db = new ApplicationDbContext(options, interceptor);

        var publishMock = new Mock<IPublishEndpoint>();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ApplicationDbContext>(db);
        services.AddSingleton(publishMock.Object);
        var sp = services.BuildServiceProvider();

        return (db, sp, publishMock);
    }

    private static OutboxRelayBackgroundService CreateService(IServiceProvider sp, OutboxOptions opts)
    {
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new OutboxRelayBackgroundService(scopeFactory, Options.Create(opts), NullLogger<OutboxRelayBackgroundService>.Instance);
    }

    private static Task InvokeProcessBatch(OutboxRelayBackgroundService svc, CancellationToken ct = default)
    {
        var method = typeof(OutboxRelayBackgroundService)
            .GetMethod("ProcessBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)method.Invoke(svc, new object[] { ct })!;
    }

    private static OutboxMessage MakeOutboxMessage<TEvt>(TEvt evt) where TEvt : SharedContracts.Events.Root.IntegrationEvent =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TEvt).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(evt),
            OccurredAt = evt.OccurredAt,
            ProcessedAt = null,
            RetryCount = 0
        };

    [Fact]
    public async Task ProcessBatch_PublishesUnprocessed_AndMarksProcessedAt()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_PublishesUnprocessed_AndMarksProcessedAt));

        await db.OutboxMessages.AddRangeAsync(
            MakeOutboxMessage(new SendOtpRegisterEvent("a@b.c", "111")),
            MakeOutboxMessage(new SendPhoneOtpEvent("0901", "222")));
        await db.SaveChangesAsync();

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 50, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        var rows = await db.OutboxMessages.ToListAsync();
        rows.Should().AllSatisfy(r => r.ProcessedAt.Should().NotBeNull());
        pub.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessBatch_PublishThrows_IncrementsRetryCount_KeepsProcessedAtNull()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_PublishThrows_IncrementsRetryCount_KeepsProcessedAtNull));

        var msg = MakeOutboxMessage(new SendOtpRegisterEvent("a@b.c", "111"));
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();

        pub.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("RabbitMQ down"));

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 50, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        var saved = await db.OutboxMessages.SingleAsync();
        saved.ProcessedAt.Should().BeNull();
        saved.RetryCount.Should().Be(1);
        saved.LastError.Should().Contain("RabbitMQ down");
    }

    [Fact]
    public async Task ProcessBatch_RetryCountAtMax_SkipsMessage()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_RetryCountAtMax_SkipsMessage));

        var msg = MakeOutboxMessage(new SendOtpRegisterEvent("a@b.c", "111"));
        msg.RetryCount = 10;  // = MaxRetries
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 50, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        pub.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessBatch_RespectsBatchSize_LimitsRowsPerTick()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_RespectsBatchSize_LimitsRowsPerTick));

        for (int i = 0; i < 10; i++)
        {
            var msg = MakeOutboxMessage(new SendOtpRegisterEvent($"u{i}@b.c", "111"));
            msg.OccurredAt = DateTime.UtcNow.AddSeconds(i);
            db.OutboxMessages.Add(msg);
        }
        await db.SaveChangesAsync();

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 3, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        var processedCount = await db.OutboxMessages.CountAsync(o => o.ProcessedAt != null);
        processedCount.Should().Be(3);
        pub.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ProcessBatch_OrderByOccurredAt_OldestFirst()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_OrderByOccurredAt_OldestFirst));

        var older = MakeOutboxMessage(new SendOtpRegisterEvent("old@b.c", "111"));
        older.OccurredAt = DateTime.UtcNow.AddMinutes(-10);
        var newer = MakeOutboxMessage(new SendOtpRegisterEvent("new@b.c", "222"));
        newer.OccurredAt = DateTime.UtcNow;

        await db.OutboxMessages.AddRangeAsync(newer, older);  // insert ngược thứ tự
        await db.SaveChangesAsync();

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 1, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        var olderReloaded = await db.OutboxMessages.FirstAsync(o => o.Id == older.Id);
        var newerReloaded = await db.OutboxMessages.FirstAsync(o => o.Id == newer.Id);
        olderReloaded.ProcessedAt.Should().NotBeNull();
        newerReloaded.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatch_UnknownEventType_IncrementsRetry_LogsError()
    {
        var (db, sp, pub) = BuildScopedDeps(nameof(ProcessBatch_UnknownEventType_IncrementsRetry_LogsError));

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "Some.Unknown.Type, NotARealAssembly",
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            RetryCount = 0
        });
        await db.SaveChangesAsync();

        var svc = CreateService(sp, new OutboxOptions { BatchSize = 50, PollIntervalSeconds = 2, MaxRetries = 10 });

        await InvokeProcessBatch(svc);

        var saved = await db.OutboxMessages.SingleAsync();
        saved.ProcessedAt.Should().BeNull();
        saved.RetryCount.Should().Be(1);
        saved.LastError.Should().Contain("Cannot resolve type");
        pub.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
