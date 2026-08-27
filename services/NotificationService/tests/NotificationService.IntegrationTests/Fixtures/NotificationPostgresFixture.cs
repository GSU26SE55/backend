using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure.Persistence;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using Testcontainers.PostgreSql;

namespace NotificationService.IntegrationTests.Fixtures;

/// <summary>
/// Postgres thật dùng chung cho toàn bộ integration test của NotificationService.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao phải là Postgres, không phải InMemory:</b> bộ unit test của service này chạy trên
/// provider InMemory, mà provider đó bỏ qua đúng những thứ bảo vệ dữ liệu ở đây — ràng buộc
/// <c>CHECK</c>, chỉ mục duy nhất một phần (<c>HasFilter</c>), và khoá ngoại. Một dòng vi phạm
/// <c>ck_notification_batch_targets_shape</c> lưu êm xuôi trên InMemory và nổ khi chạy thật.
/// </para>
/// <para>
/// <b>Một container cho cả assembly</b> (<see cref="NotificationDatabaseCollection"/>): mỗi
/// container mất vài giây để lên. Đổi lại, mỗi lớp test phải tự dọn dữ liệu — xem
/// <see cref="ResetAsync"/> — và dùng giá trị duy nhất cho các cột có ràng buộc duy nhất.
/// </para>
/// </remarks>
public sealed class NotificationPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("notification_it")
        .WithUsername("test")
        .WithPassword("test")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _pg.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    /// <summary>
    /// DbContext như lúc chạy thật — có <see cref="AuditableEntityInterceptor"/>, nên
    /// <c>CreatedAt</c>/<c>UpdatedAt</c> được điền tự động và <c>Remove</c> chuyển thành xoá mềm.
    /// Dựng context không kèm interceptor sẽ đo một đường không tồn tại trong production.
    /// </summary>
    public ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ApplicationDbContext(options, new AuditableEntityInterceptor(new NoUserCurrentUserService()));
    }

    /// <summary>
    /// Dọn bảng giữa các lớp test. Dùng <c>TRUNCATE</c> chứ không xoá qua EF: nhanh hơn, và quan
    /// trọng hơn là bỏ qua interceptor xoá mềm — xoá qua EF chỉ đánh dấu <c>is_deleted</c>, dòng
    /// vẫn nằm đó và lớp test sau sẽ nhìn thấy.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE notification_batch_targets, notification_batches, "
            + "notification_group_members, notification_groups, notification_category_preferences, "
            + "notification_preferences, notification_audit_outbox, notification_audit_logs, "
            + "push_receipts, notifications, device_tokens, account_read_models "
            + "RESTART IDENTITY CASCADE;");
    }

    /// <summary>Không có HttpContext trong test — actor để trống, interceptor chấp nhận null.</summary>
    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}

[CollectionDefinition(nameof(NotificationDatabaseCollection))]
public sealed class NotificationDatabaseCollection : ICollectionFixture<NotificationPostgresFixture>;
