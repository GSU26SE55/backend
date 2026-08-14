using System.Data.Common;
using AuditAggregatorService.Application.Consumers;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using AuditAggregatorService.Infrastructure.BackgroundJobs;
using AuditAggregatorService.Infrastructure.Implements.Repositories;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Events.Audit;
using SharedKernels.Interfaces;
using Testcontainers.PostgreSql;
using Xunit;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// Hai mảnh còn lại sau <c>AuditCreatedConsumerTests</c> và <c>UnitOfWorkTransactionTests</c>:
///
/// <list type="number">
///   <item>Nhánh <c>catch (DbUpdateException)</c> thật của consumer — nhánh "hai instance cùng
///   INSERT một event". Không dựng được bằng DB thật một cách tin cậy (kiểm tra
///   <c>AnyAsync</c> luôn chặn trước), nên ở đây mock UoW để ép đúng thứ tự cần thiết.</item>
///   <item><see cref="AuditRetentionBackgroundService"/> — vòng lặp và đường huỷ, cộng với một
///   test chứng minh <b>quy tắc D15</b> ("giữ vĩnh viễn Critical/Security") đúng trên Postgres
///   thật.</item>
/// </list>
/// </summary>
public class AuditRetentionAndRaceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_retention_test")
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

    private static AuditAggregate Agg(string severity, DateTime occurredAt) => AuditAggregate.FromEvent(
        Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", severity,
        "Account", Guid.NewGuid(), "x@example.com",
        Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
        true, null, null, null, Guid.NewGuid(), null, occurredAt, DateTime.UtcNow);

    // ───────────────────────────────── 1) nhánh catch(DbUpdateException) của consumer

    /// <summary>
    /// Dựng đúng cuộc đua: <c>AnyAsync</c> trả false (lúc kiểm tra chưa có gì), rồi
    /// <c>SaveChangesAsync</c> ném <see cref="DbUpdateException"/> vì instance khác vừa chèn xong.
    ///
    /// <para>Consumer PHẢI nuốt lỗi này. Nếu để nó bay lên, MassTransit sẽ retry rồi cuối cùng đẩy
    /// message vào <c>_error</c> queue — biến một cuộc đua vô hại thành báo động giả, và tệ hơn là
    /// làm nghẽn hàng đợi audit khi tải cao.</para>
    /// </summary>
    [Fact]
    public async Task Consume_SaveChangesThrowsUniqueViolation_IsSwallowed()
    {
        var repo = new Mock<IGenericRepository<AuditAggregate>>();
        repo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditAggregate, bool>>>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.AddAsync(It.IsAny<AuditAggregate>())).Returns(Task.CompletedTask);

        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.Setup(u => u.AuditAggregates).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(Gh775.UniqueViolationOn(DuplicateAuditDetection.EventUniqueIndexName));

        var geo = new Mock<IGeoIpResolver>();
        geo.Setup(x => x.Lookup(It.IsAny<string?>())).Returns((GeoIpResult?)null);

        var consumer = new AuditCreatedConsumer(uow.Object, geo.Object,
            NullLogger<AuditCreatedConsumer>.Instance);

        var evt = new AuditCreatedEventV1(
            Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", "Info",
            "Account", Guid.NewGuid(), "x@example.com",
            Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
            true, null, null, null, Guid.NewGuid(), null,
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        var ctx = new Mock<ConsumeContext<AuditCreatedEventV1>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var act = async () => await consumer.Consume(ctx.Object);

        await act.Should().NotThrowAsync(
            "đụng unique khi hai instance cùng chèn là chuyện bình thường — ném lên broker sẽ tạo báo động giả");
    }

    // ── GH-775 ───────────────────────────────────────────────────────────────────────────────
    // Bản cũ bắt TRỌN DbUpdateException và coi mọi thứ là đua khoá trùng. Cấu hình bảng còn có ràng
    // buộc độ dài và cột jsonb, nên ServiceName quá dài (22001) hay payload không phải JSON hợp lệ
    // (22P02) cũng rơi vào đúng nhánh đó: ACK, mất bản ghi kiểm toán, không retry, không DLQ, log
    // ghi "trùng lặp". Với hệ thống kiểm toán thì đó là kiểu mất mát tệ nhất.

    /// <summary>
    /// Dựng ngoại lệ GIỐNG THẬT: Npgsql luôn bọc một <see cref="DbException"/> mang SQLSTATE.
    /// Test cũ chỉ tạo <c>DbUpdateException("…duplicate key…")</c> — chuỗi thông điệp không phải là
    /// thứ đáng tin để phân loại lỗi, và một bản giả như vậy sẽ xanh cả với code không kiểm gì.
    /// </summary>
    private static class Gh775
    {
        private sealed class FakeDbException : DbException
        {
            private readonly string _sqlState;

            public FakeDbException(string sqlState, string message, string? constraintName)
                : base(message)
            {
                _sqlState = sqlState;
                if (constraintName is not null)
                    Data["ConstraintName"] = constraintName;
            }

            public override string SqlState => _sqlState;
        }

        public static DbUpdateException UniqueViolationOn(string constraintName)
            => new("An error occurred while saving the entity changes.",
                new FakeDbException("23505",
                    $"duplicate key value violates unique constraint \"{constraintName}\"", constraintName));

        public static DbUpdateException WithSqlState(string sqlState, string message)
            => new("An error occurred while saving the entity changes.",
                new FakeDbException(sqlState, message, constraintName: null));
    }

    private static (Mock<IAuditAggregatorUnitOfWork> Uow, Mock<IGeoIpResolver> Geo) FailingUow(Exception onSave)
    {
        var repo = new Mock<IGenericRepository<AuditAggregate>>();
        repo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditAggregate, bool>>>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.AddAsync(It.IsAny<AuditAggregate>())).Returns(Task.CompletedTask);

        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.Setup(u => u.AuditAggregates).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(onSave);

        var geo = new Mock<IGeoIpResolver>();
        geo.Setup(x => x.Lookup(It.IsAny<string?>())).Returns((GeoIpResult?)null);
        return (uow, geo);
    }

    private static ConsumeContext<AuditCreatedEventV1> Gh775Context()
    {
        var evt = new AuditCreatedEventV1(
            Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", "Info",
            "Account", Guid.NewGuid(), "x@example.com",
            Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
            true, null, null, null, Guid.NewGuid(), null,
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);

        var ctx = new Mock<ConsumeContext<AuditCreatedEventV1>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    [Theory]
    // 22001 = string_data_right_truncation — ServiceName/ActorDisplay vượt giới hạn cột.
    [InlineData("22001", "value too long for type character varying(64)")]
    // 22P02 = invalid_text_representation — MetadataJson không phải JSON hợp lệ cho cột jsonb.
    [InlineData("22P02", "invalid input syntax for type json")]
    // 23503 = foreign_key_violation — lỗi dữ liệu thật, không phải trùng.
    [InlineData("23503", "insert or update violates foreign key constraint")]
    // 40001 = serialization_failure — lỗi TẠM THỜI, phải retry chứ không được nuốt.
    [InlineData("40001", "could not serialize access due to concurrent update")]
    public async Task Consume_RealDatabaseError_IsRethrown_NotSwallowedAsDuplicate(string sqlState, string message)
    {
        var (uow, geo) = FailingUow(Gh775.WithSqlState(sqlState, message));
        var consumer = new AuditCreatedConsumer(uow.Object, geo.Object, NullLogger<AuditCreatedConsumer>.Instance);

        var act = async () => await consumer.Consume(Gh775Context());

        await act.Should().ThrowAsync<DbUpdateException>(
            $"SQLSTATE {sqlState} là lỗi DB thật — nuốt nó là mất bản ghi kiểm toán trong im lặng");
    }

    [Fact]
    public async Task Consume_UniqueViolationOnADifferentConstraint_IsRethrown()
    {
        // Vi phạm unique ở ràng buộc KHÁC không phải chuyện trùng event; nuốt nó là che mất lỗi dữ liệu.
        var (uow, geo) = FailingUow(Gh775.UniqueViolationOn("ux_some_other_constraint"));
        var consumer = new AuditCreatedConsumer(uow.Object, geo.Object, NullLogger<AuditCreatedConsumer>.Instance);

        var act = async () => await consumer.Consume(Gh775Context());

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Consume_DbUpdateExceptionWithoutSqlState_IsRethrown()
    {
        // Không đọc được mã lỗi ⇒ KHÔNG có cơ sở nào để gọi là trùng lặp. Fail closed: ném lên để
        // message vào retry/DLQ, thay vì im lặng vứt một bản ghi kiểm toán.
        var (uow, geo) = FailingUow(new DbUpdateException("duplicate key value violates unique constraint"));
        var consumer = new AuditCreatedConsumer(uow.Object, geo.Object, NullLogger<AuditCreatedConsumer>.Instance);

        var act = async () => await consumer.Consume(Gh775Context());

        await act.Should().ThrowAsync<DbUpdateException>(
            "chuỗi thông điệp không phải căn cứ để phân loại lỗi");
    }

    /// <summary>
    /// Lỗi KHÁC <see cref="DbUpdateException"/> (vd mất kết nối DB) thì PHẢI bay lên để MassTransit
    /// retry. Nuốt hết mọi loại lỗi là đánh mất event audit trong im lặng — đúng thứ mà hệ thống
    /// audit không được phép làm.
    /// </summary>
    [Fact]
    public async Task Consume_NonDbUpdateException_IsRethrown_SoBrokerCanRetry()
    {
        var repo = new Mock<IGenericRepository<AuditAggregate>>();
        repo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AuditAggregate, bool>>>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.AddAsync(It.IsAny<AuditAggregate>())).Returns(Task.CompletedTask);

        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.Setup(u => u.AuditAggregates).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(new TimeoutException("mất kết nối tới Postgres"));

        var geo = new Mock<IGeoIpResolver>();
        geo.Setup(x => x.Lookup(It.IsAny<string?>())).Returns((GeoIpResult?)null);

        var consumer = new AuditCreatedConsumer(uow.Object, geo.Object,
            NullLogger<AuditCreatedConsumer>.Instance);

        var evt = new AuditCreatedEventV1(
            Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", "Info",
            null, null, null, null, null, null, null, null,
            true, null, null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow);

        var ctx = new Mock<ConsumeContext<AuditCreatedEventV1>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var act = async () => await consumer.Consume(ctx.Object);

        await act.Should().ThrowAsync<TimeoutException>(
            "lỗi hạ tầng phải nổi lên để broker retry — nuốt đi là mất event audit trong im lặng");
    }

    // ───────────────────────────────── 2) AuditRetentionBackgroundService

    /// <summary>
    /// Lớp con rút nhịp xuống nửa giây và mở cửa sổ bảo trì. Hai thành viên đó khai
    /// <c>protected virtual</c> trong mã production <b>chỉ để test chạm được thân vòng lặp</b> —
    /// giá trị mặc định (6 giờ, khung 03:00–04:00 UTC) không đổi.
    ///
    /// <para><paramref name="windowOpen"/> cho phép dựng riêng nhánh "ngoài giờ bảo trì" — nhánh đó
    /// phải <c>continue</c>, tức KHÔNG xoá gì. Không tách được thì test sẽ phụ thuộc vào giờ chạy
    /// thật và đỏ ngẫu nhiên.</para>
    /// </summary>
    private sealed class TestableRetentionService(
        IServiceScopeFactory factory,
        Microsoft.Extensions.Logging.ILogger<AuditRetentionBackgroundService> logger,
        bool windowOpen)
        : AuditRetentionBackgroundService(factory, logger)
    {
        // GH-729 — seam đổi từ CheckInterval sang DelayUntilNextRun (service nay ngủ tới đúng
        // mốc 03:00 UTC thay vì tick chu kỳ). Rút ngắn để test không phải chờ tới 3 giờ sáng.
        protected override TimeSpan DelayUntilNextRun(DateTime utcNow) => TimeSpan.FromMilliseconds(500);
        protected override bool IsWithinMaintenanceWindow(DateTime utcNow) => windowOpen;
    }

    private ServiceProvider BuildProvider() => new ServiceCollection()
        .AddDbContext<AuditAggregateDbContext>(o => o.UseNpgsql(_pg.GetConnectionString()))
        .AddScoped<IAuditAggregatorUnitOfWork, UnitOfWork>()
        .AddLogging()
        .BuildServiceProvider(true);

    [Fact]
    public async Task RetentionService_StartsAndStopsGracefully()
    {
        await using var provider = BuildProvider();

        var service = new TestableRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditRetentionBackgroundService>.Instance,
            windowOpen: false);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1200);

        var stop = async () => await service.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync("huỷ phải thoát êm, không được ném ra ngoài");

        service.Dispose();
    }

    /// <summary>
    /// Chạy THẬT thân vòng lặp trong cửa sổ bảo trì: bản ghi cũ thường bị xoá, bản ghi
    /// <c>Critical</c>/<c>Security</c> ở lại vĩnh viễn (quy tắc D15).
    /// </summary>
    [Fact]
    public async Task RetentionService_InsideWindow_DeletesOldRows_ButKeepsCriticalAndSecurity()
    {
        var veryOld = DateTime.UtcNow.AddDays(-400);

        await using (var seed = NewContext())
        {
            seed.AuditAggregates.AddRange(
                Agg("Info", veryOld),
                Agg("Warning", veryOld),
                Agg("Critical", veryOld),
                Agg("Security", veryOld),
                Agg("Info", DateTime.UtcNow.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProvider();
        var service = new TestableRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditRetentionBackgroundService>.Instance,
            windowOpen: true);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                await using var probe = NewContext();
                if (await probe.AuditAggregates.CountAsync() == 3)
                    break;
                await Task.Delay(200);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }

        await using var verify = NewContext();
        var remaining = await verify.AuditAggregates.AsNoTracking().Select(x => x.Severity).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { "Critical", "Security", "Info" },
            "D15: Critical/Security giữ vĩnh viễn; Info mới (10 ngày) chưa tới hạn");
    }

    /// <summary>
    /// Ngoài cửa sổ bảo trì thì tuyệt đối KHÔNG được xoá gì — chốt chặn giờ tồn tại để việc xoá
    /// hàng loạt không rơi vào giờ cao điểm.
    /// </summary>
    [Fact]
    public async Task RetentionService_OutsideWindow_DeletesNothing()
    {
        await using (var seed = NewContext())
        {
            seed.AuditAggregates.Add(Agg("Info", DateTime.UtcNow.AddDays(-400)));
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProvider();
        var service = new TestableRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditRetentionBackgroundService>.Instance,
            windowOpen: false);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(2000); // vài nhịp
        await service.StopAsync(CancellationToken.None);
        service.Dispose();

        await using var verify = NewContext();
        (await verify.AuditAggregates.CountAsync()).Should().Be(1,
            "ngoài giờ bảo trì phải đứng yên, dù bản ghi đã quá hạn");
    }

    /// <summary>
    /// <b>Quy tắc D15</b>: xoá bản ghi cũ hơn 180 ngày, NHƯNG giữ vĩnh viễn <c>Critical</c> và
    /// <c>Security</c>. Chạy đúng câu truy vấn mà background service dùng, trên Postgres thật.
    ///
    /// <para>Đây là quy tắc pháp lý/forensic — xoá nhầm một bản ghi Security cũ là mất bằng chứng
    /// không lấy lại được. Vì vậy nó phải có test riêng, bất kể background service có chạy trong
    /// test hay không.</para>
    /// </summary>
    [Fact]
    public async Task RetentionRule_DeletesOldRows_ButKeepsCriticalAndSecurityForever()
    {
        var veryOld = DateTime.UtcNow.AddDays(-400);
        var recent = DateTime.UtcNow.AddDays(-10);

        await using (var seed = NewContext())
        {
            seed.AuditAggregates.AddRange(
                Agg("Info", veryOld),        // cũ + thường  → phải xoá
                Agg("Warning", veryOld),     // cũ + thường  → phải xoá
                Agg("Critical", veryOld),    // cũ + Critical → PHẢI GIỮ
                Agg("Security", veryOld),    // cũ + Security → PHẢI GIỮ
                Agg("Info", recent));        // mới          → phải giữ
            await seed.SaveChangesAsync();
        }

        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(180);

        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            var deleted = await uow.AuditAggregates.GetAllAsync()
                .Where(x => x.OccurredAt < cutoff && x.Severity != "Critical" && x.Severity != "Security")
                .ExecuteDeleteAsync();

            deleted.Should().Be(2, "chỉ Info và Warning cũ mới bị xoá");
        }

        await using (var verify = NewContext())
        {
            var remaining = await verify.AuditAggregates.AsNoTracking()
                .Select(x => x.Severity).ToListAsync();

            remaining.Should().BeEquivalentTo(new[] { "Critical", "Security", "Info" });
            remaining.Should().Contain("Critical", "D15: Critical giữ vĩnh viễn bất kể bao nhiêu tuổi");
            remaining.Should().Contain("Security", "D15: Security giữ vĩnh viễn bất kể bao nhiêu tuổi");
        }
    }
}
