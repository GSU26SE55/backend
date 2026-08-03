using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedContracts.Events.Root;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.BackgroundJobs;
using SmsService.Infrastructure.Persistence;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.BackgroundJobs;

/// <summary>
/// <see cref="OutboxRelayBackgroundService"/> — trước bộ test này phủ 0%, tức 80 dòng của cơ chế
/// bảo đảm "không mất event" chưa từng được chạy lần nào.
///
/// <para><b>Chạy được vòng lặp mà không phải chờ:</b> nhịp poll đọc từ
/// <c>IOptions&lt;OutboxOptions&gt;.PollIntervalSeconds</c>, nên test đặt xuống 1 giây. Không phải
/// sửa mã production để test — đây là khe cấu hình vốn đã có sẵn.</para>
///
/// <para><b>Vì sao đo bằng trạng thái DB chứ không bằng "đã gọi Publish":</b> hợp đồng thật của
/// outbox là <c>ProcessedAt</c>/<c>RetryCount</c>/<c>LastError</c> trong bảng — đó là thứ quyết định
/// event có bị gửi lại hay không sau khi service khởi động lại. Đếm số lần gọi hàm không nói được
/// điều đó.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class OutboxRelayBackgroundServiceTests : IAsyncLifetime
{
    private readonly SmsPostgresFixture _db;
    public OutboxRelayBackgroundServiceTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Event thật của hệ thống, có kiểu phân giải được qua <c>Type.GetType</c>.</summary>
    private static OutboxMessage Pending(string? eventTypeOverride = null, string? payloadOverride = null)
    {
        var evt = new TestOutboxEvent { Note = "xin chao" };
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventTypeOverride ?? typeof(TestOutboxEvent).AssemblyQualifiedName!,
            Payload = payloadOverride ?? JsonSerializer.Serialize(evt),
            OccurredAt = DateTime.UtcNow.AddSeconds(-1),
            ProcessedAt = null,
            RetryCount = 0,
        };
    }

    private async Task<ServiceProvider> BuildAsync(
        Action<IServiceCollection>? extra = null, int maxRetries = 10)
    {
        var services = new ServiceCollection()
            .AddDbContext<SmsDbContext>(o => o.UseNpgsql(_db.ConnectionString))
            // SmsDbContext yêu cầu AuditableEntityInterceptor ở constructor runtime; production lấy
            // nó (và ICurrentUserService) từ AddSharedInfrastructure. Thiếu hai đăng ký này thì
            // DbContext không dựng được, ProcessBatchAsync ném ngay dòng đầu, và relay NUỐT lỗi ở
            // catch chung → test chỉ thấy "không có gì xảy ra" mà không biết vì sao.
            .AddScoped<ICurrentUserService, NoUserCurrentUserService>()
            .AddScoped<AuditableEntityInterceptor>()
            .AddLogging()
            .AddMassTransitTestHarness(x =>
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)));

        services.Configure<OutboxOptions>(o =>
        {
            o.PollIntervalSeconds = 1; // nhịp nhanh để test không phải chờ
            o.BatchSize = 50;
            o.MaxRetries = maxRetries;
        });

        extra?.Invoke(services);

        var provider = services.BuildServiceProvider(true);

        // Chốt DI TRƯỚC khi chạy relay: nếu scope không dựng nổi DbContext hay IPublishEndpoint thì
        // hỏng ở đây, thay vì biến thành một test hết giờ khó chẩn đoán.
        using (var probe = provider.CreateScope())
        {
            probe.ServiceProvider.GetRequiredService<SmsDbContext>().Should().NotBeNull();
            probe.ServiceProvider.GetRequiredService<IPublishEndpoint>().Should().NotBeNull();
        }

        await provider.GetRequiredService<ITestHarness>().Start();
        return provider;
    }

    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }

    private OutboxRelayBackgroundService NewRelay(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IOptions<OutboxOptions>>(),
        NullLogger<OutboxRelayBackgroundService>.Instance);

    /// <summary>Chạy relay tới khi <paramref name="until"/> đúng, hoặc hết hạn.</summary>
    private static async Task RunUntilAsync(OutboxRelayBackgroundService relay, Func<Task<bool>> until,
        int timeoutSeconds = 20)
    {
        await relay.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await until())
                    return;
                await Task.Delay(200);
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
    public async Task PendingMessage_IsPublished_AndMarkedProcessed()
    {
        var msg = Pending();
        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(msg);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();
        var harness = provider.GetRequiredService<ITestHarness>();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.OutboxMessages.AnyAsync(o => o.Id == msg.Id && o.ProcessedAt != null);
        });

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync(o => o.Id == msg.Id);
        row.ProcessedAt.Should().NotBeNull("đã publish thì phải đánh dấu, nếu không tick sau sẽ gửi lại");
        row.LastError.Should().BeNull();
        row.RetryCount.Should().Be(0);

        (await harness.Published.Any<TestOutboxEvent>()).Should().BeTrue("event phải thật sự lên bus");
    }

    [Fact]
    public async Task AlreadyProcessedMessage_IsNotPublishedAgain()
    {
        var done = Pending();
        done.ProcessedAt = DateTime.UtcNow.AddMinutes(-5);

        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(done);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();
        var harness = provider.GetRequiredService<ITestHarness>();

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(2500); // đủ cho ít nhất một nhịp
        await relay.StopAsync(CancellationToken.None);
        relay.Dispose();

        (await harness.Published.SelectAsync<TestOutboxEvent>().Take(0).Count()).Should().Be(0);
        harness.Published.Select<TestOutboxEvent>().Should().BeEmpty(
            "message đã xử lý không được publish lại — đây chính là điều chống trùng của outbox");
    }

    // ─────────────────────────────────────────────────────────── đường hỏng

    /// <summary>
    /// Kiểu event không phân giải được (assembly đã đổi tên, event bị xoá…). Phải tăng
    /// <c>RetryCount</c> + ghi <c>LastError</c>, KHÔNG được đánh dấu đã xử lý và KHÔNG được làm chết
    /// vòng lặp — một message hỏng không được kéo theo toàn bộ hàng đợi.
    /// </summary>
    [Fact]
    public async Task UnresolvableEventType_IncrementsRetry_AndKeepsUnprocessed()
    {
        var bad = Pending(eventTypeOverride: "Khong.Ton.Tai.MotEventNaoDo, KhongCoAssembly");
        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(bad);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.OutboxMessages.AnyAsync(o => o.Id == bad.Id && o.RetryCount > 0);
        });

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync(o => o.Id == bad.Id);
        row.ProcessedAt.Should().BeNull("không publish được thì tuyệt đối không được đánh dấu đã xử lý");
        row.RetryCount.Should().BeGreaterThan(0);
        row.LastError.Should().Contain("Cannot resolve type");
    }

    /// <summary>
    /// Payload là <c>"null"</c> hợp lệ về mặt JSON nhưng deserialize ra <c>null</c> — nhánh riêng
    /// trong relay, khác với nhánh ném exception.
    /// </summary>
    [Fact]
    public async Task PayloadDeserializingToNull_IncrementsRetry_WithItsOwnMessage()
    {
        var nullPayload = Pending(payloadOverride: "null");
        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(nullPayload);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.OutboxMessages.AnyAsync(o => o.Id == nullPayload.Id && o.RetryCount > 0);
        });

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync(o => o.Id == nullPayload.Id);
        row.ProcessedAt.Should().BeNull();
        row.LastError.Should().Contain("Deserialize returned null");
    }

    /// <summary>
    /// Payload rác (không parse được JSON) → <c>JsonException</c> → rơi vào <c>catch</c> chung.
    /// Chốt rằng ngoại lệ được ghi vào <c>LastError</c> chứ không thoát ra làm chết vòng lặp.
    /// </summary>
    [Fact]
    public async Task MalformedPayload_IsCaught_AndRecordedAsError()
    {
        var broken = Pending(payloadOverride: "{ day khong phai json }");
        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(broken);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync();

        await RunUntilAsync(NewRelay(provider), async () =>
        {
            await using var db = _db.NewContext();
            return await db.OutboxMessages.AnyAsync(o => o.Id == broken.Id && o.RetryCount > 0);
        });

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync(o => o.Id == broken.Id);
        row.ProcessedAt.Should().BeNull();
        row.LastError.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Message đã chạm trần <c>MaxRetries</c> phải bị BỎ QUA hẳn ở các tick sau. Không có chốt này
    /// thì một message độc sẽ được thử lại mãi mãi, mỗi 1–2 giây, cho tới khi ai đó xoá tay.
    /// </summary>
    [Fact]
    public async Task MessageAtMaxRetries_IsSkippedEntirely()
    {
        var poisoned = Pending(eventTypeOverride: "Khong.Ton.Tai, KhongCoAssembly");
        poisoned.RetryCount = 3;

        await using (var seed = _db.NewContext())
        {
            seed.OutboxMessages.Add(poisoned);
            await seed.SaveChangesAsync();
        }

        await using var provider = await BuildAsync(maxRetries: 3);

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(2500);
        await relay.StopAsync(CancellationToken.None);
        relay.Dispose();

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync(o => o.Id == poisoned.Id);
        row.RetryCount.Should().Be(3, "chạm trần rồi thì không được thử lại nữa — nếu không sẽ quay vòng vô tận");
    }

    [Fact]
    public async Task EmptyOutbox_TicksQuietly_AndStopsGracefully()
    {
        await using var provider = await BuildAsync();

        var relay = NewRelay(provider);
        await relay.StartAsync(CancellationToken.None);
        await Task.Delay(2500);

        var stop = async () => await relay.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync("outbox rỗng là trạng thái bình thường nhất, không được ném gì");
        relay.Dispose();
    }
}

/// <summary>
/// Event tối giản dùng riêng cho test relay. Phải là kiểu THẬT (không phải mock) vì relay phân giải
/// kiểu từ chuỗi <c>AssemblyQualifiedName</c> lưu trong bảng rồi mới deserialize.
/// </summary>
public record TestOutboxEvent : IntegrationEvent
{
    public string Note { get; set; } = string.Empty;
}
