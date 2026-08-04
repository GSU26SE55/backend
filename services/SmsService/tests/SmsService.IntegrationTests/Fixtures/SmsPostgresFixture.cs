using Microsoft.EntityFrameworkCore;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using SmsService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SmsService.IntegrationTests.Fixtures;

/// <summary>
/// Postgres THẬT dùng chung cho toàn bộ integration test của SmsService.
///
/// <para><b>Vì sao phải là Postgres, không phải InMemory/SQLite:</b> phần lớn mã hạ tầng chưa được
/// phủ của service này chỉ tồn tại khi có một DbContext thật — cấu hình mapping EF
/// (<c>IEntityTypeConfiguration</c>) chỉ chạy lúc EF dựng model, còn <c>BeginTransaction</c> /
/// <c>Commit</c> / <c>Rollback</c> là hành vi của provider. Test cũ đều mock <c>IUnitOfWork</c> nên
/// không có gì trong nhóm đó từng được chạy.</para>
///
/// <para><b>Dùng chung một container cho cả assembly</b> (<see cref="SmsDatabaseCollection"/>):
/// mỗi container mất vài giây để lên; mỗi lớp test một container thì tổng thời gian chạy phồng lên
/// vô ích. Đổi lại, các test phải tự cô lập dữ liệu của mình — xem <see cref="ResetAsync"/>.</para>
/// </summary>
public sealed class SmsPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("sms_it")
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
    /// DbContext runtime — có <see cref="AuditableEntityInterceptor"/> như production, nên
    /// <c>CreatedAt</c>/<c>UpdatedAt</c> được set tự động và <c>Remove</c> bị chuyển thành xoá mềm.
    /// Dùng constructor không-interceptor (design-time) sẽ đo nhầm một đường không tồn tại thật.
    /// </summary>
    public SmsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SmsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new SmsDbContext(options, new AuditableEntityInterceptor(new NoUserCurrentUserService()));
    }

    /// <summary>
    /// Dọn sạch bảng giữa các test. Dùng <c>TRUNCATE</c> thay vì xoá qua EF: nhanh hơn, và quan
    /// trọng hơn là bỏ qua interceptor xoá-mềm — xoá qua EF chỉ đánh dấu <c>IsDeleted</c>, dữ liệu
    /// vẫn nằm đó và test sau sẽ thấy.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE sms_messages, sms_gateway_devices, sms_audit_logs, sms_audit_outbox, outbox_messages RESTART IDENTITY CASCADE;");
    }

    /// <summary>Không có HttpContext trong test — actor để trống, interceptor chấp nhận null.</summary>
    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}

[CollectionDefinition(nameof(SmsDatabaseCollection))]
public sealed class SmsDatabaseCollection : ICollectionFixture<SmsPostgresFixture>;
