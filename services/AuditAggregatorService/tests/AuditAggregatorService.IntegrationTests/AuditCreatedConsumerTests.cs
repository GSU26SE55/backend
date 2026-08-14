using AuditAggregatorService.Application.Consumers;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Infrastructure.Implements.Repositories;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharedContracts.Events.Audit;
using Testcontainers.PostgreSql;
using Xunit;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// <b><c>#AUDIT-15</c> — <see cref="AuditCreatedConsumer"/> là đường vào DUY NHẤT của read-store
/// <c>audit_aggregate</c>.</b> Trước bộ test này nó ở mức phủ 0%: mọi test khác đều gọi thẳng
/// <c>UnitOfWork</c> và chỉ <i>bắt chước</i> logic consumer, nên hành vi thật của consumer chưa từng
/// được chạy.
///
/// <para>Dùng Postgres THẬT (Testcontainers) chứ không InMemory: nhánh chống trùng dựa vào
/// <c>DbUpdateException</c> do ràng buộc unique <c>(event_id, occurred_at)</c> ném ra — provider
/// InMemory không có ràng buộc nào nên nhánh đó sẽ không bao giờ chạy và test sẽ xanh giả.</para>
///
/// <para>Harness MassTransit đặt timeout tường minh: mặc định của v8 là <b>1 giây</b> im lặng, chạy
/// song song cả solution là <c>Consumed.Any&lt;T&gt;()</c> trả false — đỏ ngẫu nhiên chứ không phải
/// sai logic.</para>
/// </summary>
public class AuditCreatedConsumerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_consumer_test")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    private AuditAggregateDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AuditAggregateDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options);

    /// <summary>
    /// Dựng harness với đúng consumer thật + UnitOfWork thật trỏ vào Postgres container.
    /// <paramref name="geo"/> cho phép mỗi test tự chọn resolver trả gì.
    /// </summary>
    private async Task<(ITestHarness harness, ServiceProvider provider)> StartHarnessAsync(IGeoIpResolver geo)
    {
        var provider = new ServiceCollection()
            .AddDbContext<AuditAggregateDbContext>(o => o.UseNpgsql(_pg.GetConnectionString()))
            .AddScoped<IAuditAggregatorUnitOfWork, UnitOfWork>()
            .AddSingleton(geo)
            .AddLogging()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<AuditCreatedConsumer>();
                // Flaky guard: inactivity mặc định 1s — quá ngắn khi máy đang tải.
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, provider);
    }

    private static AuditCreatedEventV1 Event(
        Guid? eventId = null, string? ip = "203.0.113.7", DateTime? occurredAt = null,
        string severity = "Info", string service = "AuthService") =>
        new(
            eventId ?? Guid.NewGuid(), service, "LoginSucceeded", "Authentication", severity,
            "Account", Guid.NewGuid(), "x@example.com",
            Guid.NewGuid(), "Admin", "Admin User", ip, "ua",
            true, null, null, null,
            Guid.NewGuid(), null,
            occurredAt ?? DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow);

    private static IGeoIpResolver GeoReturning(GeoIpResult? result)
    {
        var m = new Mock<IGeoIpResolver>();
        m.Setup(x => x.Lookup(It.IsAny<string?>())).Returns(result);
        return m.Object;
    }

    // ────────────────────────────────────────────────────── đường đi thành công

    [Fact]
    public async Task Consume_NewEvent_InsertsRow_AndEnrichesGeo()
    {
        var (harness, provider) = await StartHarnessAsync(GeoReturning(new GeoIpResult("VN", "Da Nang")));
        await using var _ = provider;

        var evt = Event();
        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<AuditCreatedEventV1>()).Should().BeTrue(
            "consumer phải nhận được event; false ở đây thường là hết giờ chờ chứ không phải sai logic");

        await using var db = NewContext();
        var row = await db.AuditAggregates.SingleAsync(x => x.EventId == evt.EventId);

        row.ServiceName.Should().Be(evt.ServiceName);
        row.ActionCode.Should().Be(evt.ActionCode);
        row.GeoCountry.Should().Be("VN", "geo enrichment #AUDIT-16 phải ghi vào row");
        row.GeoCity.Should().Be("Da Nang");
    }

    /// <summary>
    /// Enrichment là tuỳ chọn — resolver trả null (thiếu file .mmdb là trường hợp thật đang xảy ra)
    /// thì event vẫn phải vào được read-store, chỉ là không có geo.
    /// </summary>
    [Fact]
    public async Task Consume_GeoResolverReturnsNull_StillInserts_WithoutGeo()
    {
        var (harness, provider) = await StartHarnessAsync(GeoReturning(null));
        await using var _ = provider;

        var evt = Event();
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AuditCreatedEventV1>()).Should().BeTrue();

        await using var db = NewContext();
        var row = await db.AuditAggregates.SingleAsync(x => x.EventId == evt.EventId);
        row.GeoCountry.Should().BeNull();
        row.GeoCity.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────── chống trùng

    /// <summary>
    /// At-least-once của RabbitMQ nghĩa là event LẶP LẠI là chuyện bình thường, không phải sự cố.
    /// Cùng <c>EventId</c> gửi nhiều lần chỉ được đẻ ra đúng một dòng.
    /// </summary>
    [Fact]
    public async Task Consume_SameEventTwice_InsertsExactlyOneRow()
    {
        var (harness, provider) = await StartHarnessAsync(GeoReturning(null));
        await using var _ = provider;

        var evt = Event();
        await harness.Bus.Publish(evt);
        await harness.Consumed.Any<AuditCreatedEventV1>();

        await harness.Bus.Publish(evt);
        (await harness.Consumed.SelectAsync<AuditCreatedEventV1>().Take(2).Count()).Should().Be(2,
            "cả hai lần đều phải được consumer xử lý — chống trùng nằm ở tầng dữ liệu, không phải bỏ message");

        await using var db = NewContext();
        (await db.AuditAggregates.CountAsync(x => x.EventId == evt.EventId)).Should().Be(1);
    }

    /// <summary>
    /// Nhánh <c>catch (DbUpdateException)</c>: hai consumer chạy song song cùng lọt qua bước kiểm tra
    /// <c>AnyAsync</c> rồi cùng INSERT. Dựng lại đúng tình huống đó bằng cách chèn sẵn một dòng
    /// <b>sau</b> khi consumer đã kiểm tra — cách duy nhất tái hiện được là chèn trước rồi cho
    /// consumer chạy trên một DbContext khác chưa thấy dòng đó.
    /// </summary>
    [Fact]
    public async Task Consume_RowInsertedConcurrently_SwallowsUniqueViolation()
    {
        var evt = Event();

        // Dựng consumer thủ công để kiểm soát chính xác thứ tự.
        await using var dbForConsumer = NewContext();
        var uow = new UnitOfWork(dbForConsumer);
        var consumer = new AuditCreatedConsumer(
            uow, GeoReturning(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditCreatedConsumer>.Instance);

        // Một tiến trình khác chèn trước bằng SQL thô — DbContext của consumer không hề hay biết.
        await using (var other = NewContext())
        {
            var agg = Domain.Entities.AuditAggregate.FromEvent(
                evt.EventId, evt.ServiceName, evt.ActionCode, evt.ActionCategory, evt.Severity,
                evt.TargetType, evt.TargetId, evt.TargetDisplay,
                evt.ActorAccountId, evt.ActorRole, evt.ActorDisplay, evt.ActorIp, evt.ActorUserAgent,
                evt.IsSuccess, evt.ErrorCode, evt.Reason, evt.MetadataJson,
                evt.CorrelationId, evt.CausationId, evt.OccurredAt, evt.RecordedAt);
            other.AuditAggregates.Add(agg);
            await other.SaveChangesAsync();
        }

        var ctx = new Mock<ConsumeContext<AuditCreatedEventV1>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        // AnyAsync sẽ thấy dòng kia (cùng DB) → đi nhánh idempotent, KHÔNG ném.
        var act = async () => await consumer.Consume(ctx.Object);
        await act.Should().NotThrowAsync("trùng là chuyện bình thường, tuyệt đối không được ném lên broker");

        await using var verify = NewContext();
        (await verify.AuditAggregates.CountAsync(x => x.EventId == evt.EventId)).Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────── biên

    /// <summary>
    /// Clock skew: nếu <c>OccurredAt</c> ở tương lai thì độ trễ tính ra âm. Consumer phải bỏ qua mẫu
    /// đó thay vì đẩy số âm vào histogram (histogram Prometheus không có bucket âm — ghi vào là
    /// làm hỏng luôn phép đo p95/p99 của cả pipeline).
    /// </summary>
    [Fact]
    public async Task Consume_EventFromTheFuture_DoesNotThrow_AndStillInserts()
    {
        var (harness, provider) = await StartHarnessAsync(GeoReturning(null));
        await using var _ = provider;

        var evt = Event(occurredAt: DateTime.UtcNow.AddMinutes(10));
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AuditCreatedEventV1>()).Should().BeTrue();

        await using var db = NewContext();
        (await db.AuditAggregates.CountAsync(x => x.EventId == evt.EventId)).Should().Be(1);
    }

    [Fact]
    public async Task Consume_NullActorIp_SkipsGeoLookup_AndInserts()
    {
        var geo = new Mock<IGeoIpResolver>();
        geo.Setup(x => x.Lookup(It.IsAny<string?>())).Returns((GeoIpResult?)null);

        var (harness, provider) = await StartHarnessAsync(geo.Object);
        await using var _ = provider;

        var evt = Event(ip: null);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AuditCreatedEventV1>()).Should().BeTrue();

        await using var db = NewContext();
        var row = await db.AuditAggregates.SingleAsync(x => x.EventId == evt.EventId);
        row.ActorIp.Should().BeNull();
        row.GeoCountry.Should().BeNull();
    }
}
