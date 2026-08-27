using Microsoft.EntityFrameworkCore;
using NotificationService.IntegrationTests.Fixtures;

namespace NotificationService.IntegrationTests.Migrations;

/// <summary>
/// Bộ migration áp được lên một cơ sở dữ liệu trống, và mô hình EF khớp với lược đồ sinh ra.
/// </summary>
/// <remarks>
/// Service này tự chạy migration lúc khởi động, nên một migration hỏng không lộ ra ở bước build —
/// nó lộ ra lúc container không lên được. Fixture đã gọi <c>MigrateAsync</c> trên một cơ sở dữ
/// liệu trống, các bài dưới đây khẳng định kết quả của lần chạy đó.
/// </remarks>
[Trait("Category", "Migration")]
[Collection(nameof(NotificationDatabaseCollection))]
public class MigrationSmokeTests
{
    private readonly NotificationPostgresFixture _db;

    public MigrationSmokeTests(NotificationPostgresFixture db) => _db = db;

    [Fact]
    public async Task AllMigrations_AreApplied_AndNoneIsLeftPending()
    {
        await using var db = _db.NewContext();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        applied.Should().NotBeEmpty();
        pending.Should().BeEmpty("migration còn treo nghĩa là container sẽ chạy nó lúc khởi động thật");
    }

    /// <summary>
    /// Mọi bảng mà mô hình EF khai báo đều phải tồn tại thật sau khi migration chạy. Lệch nghĩa là
    /// có ai đó đổi entity mà quên sinh migration — máy họ vẫn chạy tốt vì cơ sở dữ liệu ở đó đã
    /// có sẵn bảng từ lần trước, còn môi trường dựng mới thì hỏng.
    /// </summary>
    [Fact]
    public async Task EveryTableInTheModel_ExistsInTheDatabase()
    {
        await using var db = _db.NewContext();

        var declared = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        declared.Should().NotBeEmpty();

        await db.Database.OpenConnectionAsync();
        var missing = new List<string>();
        foreach (var table in declared)
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT to_regclass('public.' || @name) IS NOT NULL;";
            var p = cmd.CreateParameter();
            p.ParameterName = "name";
            p.Value = table!;
            cmd.Parameters.Add(p);
            if (!(bool)(await cmd.ExecuteScalarAsync())!)
                missing.Add(table!);
        }

        missing.Should().BeEmpty("entity đã khai bảng mà migration chưa tạo ra bảng đó");
    }

    [Theory]
    [InlineData("notifications")]
    [InlineData("device_tokens")]
    [InlineData("notification_preferences")]
    [InlineData("notification_category_preferences")]
    [InlineData("notification_templates")]
    [InlineData("notification_groups")]
    [InlineData("notification_group_members")]
    [InlineData("notification_batches")]
    [InlineData("notification_batch_targets")]
    [InlineData("notification_settings")]
    [InlineData("notification_audit_logs")]
    [InlineData("notification_audit_outbox")]
    [InlineData("push_receipts")]
    [InlineData("account_read_models")]
    public async Task Table_Exists(string table)
    {
        await using var db = _db.NewContext();
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT to_regclass('public.' || @name) IS NOT NULL;";
        var p = cmd.CreateParameter();
        p.ParameterName = "name";
        p.Value = table;
        cmd.Parameters.Add(p);

        var exists = (bool)(await cmd.ExecuteScalarAsync())!;

        exists.Should().BeTrue();
    }

    [Theory]
    [InlineData("ck_notification_groups_role_filter")]
    [InlineData("ck_notification_batch_targets_shape")]
    public async Task CheckConstraint_Exists(string constraint)
    {
        await using var db = _db.NewContext();
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = @name AND contype = 'c';";
        var p = cmd.CreateParameter();
        p.ParameterName = "name";
        p.Value = constraint;
        cmd.Parameters.Add(p);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        count.Should().Be(1);
    }

    /// <summary>
    /// Hai chỉ mục duy nhất phải là chỉ mục <b>một phần</b>. Bỏ mất mệnh đề lọc thì tên nhóm đã
    /// xoá không dùng lại được nữa, còn seeder vai trò sẽ vỡ ở lần chạy thứ hai.
    /// </summary>
    [Theory]
    [InlineData("ux_notification_groups_normalized_name", "is_deleted = false")]
    [InlineData("ux_notification_groups_role_filter", "kind = 2")]
    public async Task UniqueIndex_IsPartial(string index, string fragment)
    {
        await using var db = _db.NewContext();
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND indexname=@name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "name";
        p.Value = index;
        cmd.Parameters.Add(p);

        var definition = (string?)await cmd.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().Contain("UNIQUE INDEX");
        definition.Should().Contain("WHERE");
        definition.Should().Contain(fragment);
    }
}
