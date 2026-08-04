using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Events.Audit;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;
using SmsService.Infrastructure.BackgroundJobs;
using SmsService.Infrastructure.Persistence;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.BackgroundJobs;

/// <summary>
/// <see cref="SmsAuditOutboxRelayBackgroundService"/> (<c>#AUDIT-35</c>) — trước bộ test này phủ 0%.
///
/// <para>Đây là đường duy nhất đưa audit của SmsService lên <c>AuditAggregatorService</c>. Nó im
/// lặng thì audit mất mà không ai biết, vì bảng outbox vẫn có dữ liệu và không có lỗi nào nổi lên.</para>
///
/// <para><b>Chạy được mà không phải chờ lâu:</b> nhịp poll là hằng số 2 giây, đủ ngắn để test đợi
/// thật. <b>Bầu chủ (leader election)</b> qua <c>IDistributedCache</c> — test dùng bản in-memory,
/// không đụng Redis; nhánh "không phải chủ" và nhánh "cache lỗi → vẫn chạy" đều được dựng riêng.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class SmsAuditOutboxRelayBackgroundServiceTests : IAsyncLifetime
{
    private const string LeaderKey = "sms_audit_outbox_leader";

    private readonly SmsPostgresFixture _db;
    public SmsAuditOutboxRelayBackgroundServiceTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static AuditCreatedEventV1 AuditEvent() => new(
        Guid.NewGuid(), "SmsService", "SmsQueued", "DataModification", "Info",
        "SmsMessage", Guid.NewGuid(), "0901234567",
        Guid.NewGuid(), "System", "System", null, null,
        true, null, null, null,
        Guid.NewGuid(), null, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

    private static SmsAuditOutbox Pending(string? payloadOverride = null, int retryCount = 0)
    {
        var evt = AuditEvent();
        return new SmsAuditOutbox
        {
            Id = Guid.NewGuid(),
            EventId = evt.EventId,
            EventType = nameof(AuditCreatedEventV1),
            Payload = payloadOverride ?? JsonSerializer.Serialize(evt),
            Status = AuditOutboxStatusEnum.Pending,
            RetryCount = retryCount,
        };
    }

    private async Task<ServiceProvider> BuildAsync(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection()
            .AddDbContext<SmsDbContext>(o => o.UseNpgsql(_db.ConnectionString))
            .AddScoped<ICurrentUserService, NoUserCurrentUserService>()
            .AddScoped<AuditableEntityInterceptor>()
            .AddDistributedMemoryCache()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));

        extra?.Invoke(services);

        var provider = services.BuildServiceProvider(true);

        using (var probe = provider.CreateScope())
        {
            probe.ServiceProvider.GetRequiredService<SmsDbContext>().Should().NotBeNull();
            probe.ServiceProvider.GetRequiredService<IPublishEndpoint>().Should().NotBeNull();
        }

        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    private static SmsAuditOutboxRelayBackgroundService NewRelay(ServiceProvider provider, IDistributedCache? cache = null) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            cache ?? provider.GetRequiredService<IDistributedCache>(),
            NullLogger<SmsAuditOutboxRelayBackgroundService>.Instance);

    private static async Task RunUntilAsync(SmsAuditOutboxRelayBackgroundService relay,
        Func<Task<bool>> until, int timeoutSeconds = 25)
    {
        await relay.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await until())
                    return;
                await Task.Delay(250);
            }
        }
        finally
        {
            await relay.StopAsync(CancellationToken.None);
            relay.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────── đường đi thành công

    [Fact]
    public async Task PendingAuditRow_IsPublished_AndMarkedPublished()
    {
        var row = Pending();
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(row);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();
        var harness = provider.GetRequiredService<ITestHarness>();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsAuditOutboxes.AnyAsync(o => o.Id == row.Id && o.Status == AuditOutboxStatusEnum.Published);
        });

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == row.Id);
        saved.Status.Should().Be(AuditOutboxStatusEnum.Published);
        saved.ProcessedAt.Should().NotBeNull();
        saved.LastError.Should().BeNull();

        (await harness.Published.Any<AuditCreatedEventV1>()).Should().BeTrue(
            "event audit phải thật sự lên bus — đây là toàn bộ mục đích của relay này");
    }

    [Fact]
    public async Task AlreadyPublishedRow_IsNotSentAgain()
    {
        var done = Pending();
        done.Status = AuditOutboxStatusEnum.Published;
        done.ProcessedAt = DateTime.UtcNow.AddMinutes(-1);

        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(done);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();
        var harness = provider.GetRequiredService<ITestHarness>();

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(4000); // đủ cho vài nhịp 2s
        await relay.StopAsync(CancellationToken.None);
        relay.Dispose();

        harness.Published.Select<AuditCreatedEventV1>().Should().BeEmpty(
            "chỉ dòng Pending mới được gửi — gửi lại dòng Published sẽ đẻ ra audit trùng ở aggregator");
    }

    // ─────────────────────────────────────────────────────────────── đường hỏng

    /// <summary>
    /// Payload <c>"null"</c>: deserialize hợp lệ nhưng ra <c>null</c>. Phải tăng retry và giữ
    /// <c>Pending</c> (chưa chạm trần), tuyệt đối không đánh dấu Published.
    /// </summary>
    [Fact]
    public async Task PayloadDeserializingToNull_IncrementsRetry_AndStaysPending()
    {
        var bad = Pending(payloadOverride: "null");
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(bad);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsAuditOutboxes.AnyAsync(o => o.Id == bad.Id && o.RetryCount > 0);
        });

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == bad.Id);
        saved.Status.Should().Be(AuditOutboxStatusEnum.Pending);
        saved.RetryCount.Should().BeGreaterThan(0);
        saved.LastError.Should().Contain("Deserialize AuditCreatedEventV1 returned null");
    }

    /// <summary>
    /// Đã ở retry 4/5, hỏng thêm một lần nữa là chạm trần → chuyển hẳn sang <c>Failed</c>.
    /// Không có bước này thì một dòng hỏng sẽ được thử lại mỗi 2 giây mãi mãi.
    /// </summary>
    [Fact]
    public async Task PayloadFailingAtLastRetry_IsMarkedFailed()
    {
        var almostDead = Pending(payloadOverride: "null", retryCount: 4); // MaxRetries = 5
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(almostDead);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsAuditOutboxes.AnyAsync(o => o.Id == almostDead.Id && o.Status == AuditOutboxStatusEnum.Failed);
        });

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == almostDead.Id);
        saved.Status.Should().Be(AuditOutboxStatusEnum.Failed);
        saved.RetryCount.Should().Be(5);
    }

    [Fact]
    public async Task RowAtMaxRetries_IsSkippedEntirely()
    {
        var poisoned = Pending(payloadOverride: "null", retryCount: 5);
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(poisoned);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(4000);
        await relay.StopAsync(CancellationToken.None);
        relay.Dispose();

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == poisoned.Id);
        saved.RetryCount.Should().Be(5, "chạm trần rồi thì không được đụng tới nữa");
    }

    // ──────────────────────────────────────────────────────────── bầu chủ

    /// <summary>
    /// Instance khác đang giữ khoá → instance này KHÔNG được xử lý. Thiếu chốt này thì chạy nhiều
    /// bản sao sẽ publish trùng toàn bộ audit.
    /// </summary>
    [Fact]
    public async Task WhenAnotherInstanceHoldsLease_DoesNotProcess()
    {
        var row = Pending();
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(row);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        // Khoá đã thuộc về một instance khác.
        var cache = provider.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync(LeaderKey, "mot-instance-khac");

        var relay = NewRelay(provider, cache);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(5000); // vài nhịp
        await relay.StopAsync(CancellationToken.None);
        relay.Dispose();

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == row.Id);
        saved.Status.Should().Be(AuditOutboxStatusEnum.Pending,
            "không phải chủ thì phải đứng yên — nếu không, N bản sao sẽ publish N lần");
    }

    /// <summary>
    /// Redis chết thì bầu chủ không hoạt động. Lựa chọn của hệ thống là <b>vẫn xử lý</b> — thà audit
    /// trùng (aggregator chống trùng theo <c>EventId</c>) còn hơn mất audit. Chốt lại quyết định đó
    /// để không ai lặng lẽ đảo chiều nó.
    /// </summary>
    [Fact]
    public async Task WhenCacheThrows_FallsBackToProcessing()
    {
        var row = Pending();
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(row);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        var brokenCache = new Mock<IDistributedCache>();
        brokenCache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("Redis không kết nối được"));

        await RunUntilAsync(NewRelay(provider, brokenCache.Object), async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsAuditOutboxes.AnyAsync(o => o.Id == row.Id && o.Status == AuditOutboxStatusEnum.Published);
        });

        await using var verify = _db.NewContext();
        var saved = await verify.SmsAuditOutboxes.SingleAsync(o => o.Id == row.Id);
        saved.Status.Should().Be(AuditOutboxStatusEnum.Published,
            "Redis hỏng KHÔNG được làm dừng audit — thà trùng còn hơn mất");
    }

    /// <summary>Khoá do chính instance này giữ (gia hạn) → vẫn là chủ, vẫn xử lý.</summary>
    [Fact]
    public async Task WhenLeaseIsFree_TakesLeadership_AndProcesses()
    {
        var row = Pending();
        await using (var seed = _db.NewContext())
        {
            seed.SmsAuditOutboxes.Add(row);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();
        var cache = provider.GetRequiredService<IDistributedCache>();

        await RunUntilAsync(NewRelay(provider, cache), async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsAuditOutboxes.AnyAsync(o => o.Id == row.Id && o.Status == AuditOutboxStatusEnum.Published);
        });

        (await cache.GetStringAsync(LeaderKey)).Should().NotBeNullOrEmpty(
            "chiếm được quyền chủ thì phải ghi khoá lại để instance khác biết mà đứng yên");
    }

    [Fact]
    public async Task EmptyOutbox_TicksQuietly_AndStopsGracefully()
    {
        await using var provider = await BuildAsync();

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(4000);

        var stop = async () => await relay.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
        relay.Dispose();
    }

    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}
