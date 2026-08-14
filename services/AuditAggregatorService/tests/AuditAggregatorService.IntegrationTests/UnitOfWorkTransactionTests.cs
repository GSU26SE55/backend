using AuditAggregatorService.Domain.Entities;
using AuditAggregatorService.Infrastructure.Implements.Repositories;
using AuditAggregatorService.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// Ba phương thức giao dịch của <see cref="UnitOfWork"/> trước bộ test này ở mức phủ 0%.
///
/// <para><b>Vì sao phải Postgres thật:</b> <c>BeginTransactionAsync</c> / <c>CommitAsync</c> /
/// <c>RollbackAsync</c> là hành vi của provider. Mock ra thì chỉ chứng minh "có gọi hàm", không
/// chứng minh được dữ liệu có thật sự bị huỷ khi rollback — mà đó mới là điều đáng kiểm.</para>
///
/// <para>Chỗ dễ sai nhất của lớp này là khối <c>finally</c>: nếu quên đặt lại
/// <c>_currentTransaction = null</c> thì lần <c>BeginTransactionAsync</c> kế tiếp sẽ lặng lẽ không
/// mở giao dịch mới (vì thấy đã có), và mọi thao tác sau đó chạy ngoài giao dịch mà không ai biết.
/// Các test dưới đây chốt đúng chỗ đó bằng cách dùng lại cùng một instance sau commit/rollback.</para>
/// </summary>
public class UnitOfWorkTransactionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("audit_uow_test")
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
    /// <paramref name="occurredAt"/> phải truyền tường minh khi muốn tái hiện đụng unique: khoá là
    /// <b>composite</b> <c>(event_id, occurred_at)</c> (index <c>ux_agg_event_occurred</c>), nên hai
    /// dòng cùng <c>EventId</c> mà lệch mốc thời gian vài micro-giây sẽ KHÔNG đụng nhau.
    /// </summary>
    private static AuditAggregate Agg(Guid eventId, DateTime? occurredAt = null) => AuditAggregate.FromEvent(
        eventId, "AuthService", "LoginSucceeded", "Authentication", "Info",
        "Account", Guid.NewGuid(), "x@example.com",
        Guid.NewGuid(), "Admin", "Admin User", "127.0.0.1", "ua",
        true, null, null, null, Guid.NewGuid(), null,
        occurredAt ?? DateTime.UtcNow, DateTime.UtcNow);

    private async Task<int> CountAsync(Guid eventId)
    {
        await using var db = NewContext();
        return await db.AuditAggregates.CountAsync(x => x.EventId == eventId);
    }

    [Fact]
    public async Task Commit_PersistsRow()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(id));
            await uow.CommitTransactionAsync();
        }

        (await CountAsync(id)).Should().Be(1);
    }

    [Fact]
    public async Task Rollback_DiscardsRow()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(id));
            await uow.SaveChangesAsync();          // đã ghi trong giao dịch...
            await uow.RollbackTransactionAsync();  // ...nhưng huỷ giao dịch thì phải mất
        }

        (await CountAsync(id)).Should().Be(0, "rollback phải huỷ cả thay đổi đã SaveChanges bên trong giao dịch");
    }

    /// <summary>
    /// Gọi <c>BeginTransactionAsync</c> hai lần liên tiếp: lần hai là no-op có chủ ý (không lồng
    /// giao dịch). Kiểm để nếu ai đó đổi sang ném exception thì test này gãy và buộc phải cân nhắc.
    /// </summary>
    [Fact]
    public async Task BeginTwice_IsNoOp_AndStillCommits()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(id));
            await uow.CommitTransactionAsync();
        }

        (await CountAsync(id)).Should().Be(1);
    }

    /// <summary>
    /// Sau commit, giao dịch phải được dọn sạch để chu kỳ sau mở được giao dịch MỚI. Nếu
    /// <c>finally</c> quên gán null thì lần Begin thứ hai âm thầm không mở giao dịch, và lần
    /// Rollback tiếp theo sẽ không huỷ được gì — dòng thứ hai vẫn còn.
    /// </summary>
    [Fact]
    public async Task AfterCommit_TransactionIsReset_SoNextRollbackStillWorks()
    {
        var kept = Guid.NewGuid();
        var discarded = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);

            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(kept));
            await uow.CommitTransactionAsync();

            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(discarded));
            await uow.SaveChangesAsync();
            await uow.RollbackTransactionAsync();
        }

        (await CountAsync(kept)).Should().Be(1);
        (await CountAsync(discarded)).Should().Be(0,
            "giao dịch thứ hai phải là giao dịch THẬT — nếu _currentTransaction không được reset sau commit thì dòng này sẽ lọt");
    }

    /// <summary>
    /// Rollback khi chưa mở giao dịch nào phải im lặng bỏ qua — không được ném. Đường này xảy ra
    /// thật trong khối <c>catch</c> của handler khi lỗi nổ ra TRƯỚC <c>BeginTransactionAsync</c>.
    /// </summary>
    [Fact]
    public async Task Rollback_WithoutBegin_DoesNotThrow()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        var act = async () => await uow.RollbackTransactionAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Commit_WithoutBegin_StillSavesChanges()
    {
        var id = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.AuditAggregates.AddAsync(Agg(id));
            await uow.CommitTransactionAsync(); // không Begin — chỉ SaveChanges
        }

        (await CountAsync(id)).Should().Be(1);
    }

    /// <summary>
    /// Commit hỏng (ở đây: vi phạm ràng buộc unique <c>event_id</c>) phải <b>tự rollback rồi ném
    /// lại</b>. Ném mà không rollback là để lại giao dịch treo giữ khoá trong Postgres — kiểu lỗi
    /// chỉ lộ ra khi tải cao.
    /// </summary>
    [Fact]
    public async Task Commit_OnConstraintViolation_RollsBackAndRethrows()
    {
        var duplicated = Guid.NewGuid();
        // Cùng EventId VÀ cùng OccurredAt thì mới chạm index ux_agg_event_occurred.
        var sameInstant = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        await using (var seed = NewContext())
        {
            seed.AuditAggregates.Add(Agg(duplicated, sameInstant));
            await seed.SaveChangesAsync();
        }

        var alsoInSameBatch = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var uow = new UnitOfWork(db);
            await uow.BeginTransactionAsync();
            await uow.AuditAggregates.AddAsync(Agg(alsoInSameBatch));            // dòng hợp lệ...
            await uow.AuditAggregates.AddAsync(Agg(duplicated, sameInstant));    // ...và dòng đụng unique

            var act = async () => await uow.CommitTransactionAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        (await CountAsync(alsoInSameBatch)).Should().Be(0,
            "commit hỏng phải huỷ TOÀN BỘ lô, không để lại dòng hợp lệ nằm mồ côi");
        (await CountAsync(duplicated)).Should().Be(1, "dòng gieo sẵn không được đụng tới");
    }

    [Fact]
    public void Dispose_DisposesUnderlyingContext()
    {
        var db = NewContext();
        var uow = new UnitOfWork(db);

        uow.Dispose();

        // DbContext đã dispose thì mọi truy cập phải ném — chứng minh Dispose có tác dụng thật.
        var act = () => db.AuditAggregates.FirstOrDefault();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task SaveChangesAsync_ReturnsAffectedRowCount()
    {
        await using var db = NewContext();
        var uow = new UnitOfWork(db);

        await uow.AuditAggregates.AddAsync(Agg(Guid.NewGuid()));
        await uow.AuditAggregates.AddAsync(Agg(Guid.NewGuid()));

        (await uow.SaveChangesAsync()).Should().Be(2);
    }
}
